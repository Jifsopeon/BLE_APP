using System.Buffers.Binary;
using BLE_APP.Models;
using BLE_APP.PageModels;
using BLE_APP.Services;

var decoder = new SensorPacketDecoder();

NormalPacketDecodesAllFields();
FirstByte03IsSequenceLowByte();
NegativeTemperatureDecodes();
DistanceZeroDecodesToZeroMetres();
FutureDistance1250DecodesToOnePoint25Metres();
Legacy20BytePacketIsRejected();
Correct22BytePacketIsAccepted();
MultipleSequentialPacketsDecode();
OneReadingAddsOnePointToAllChartSeries();
AllChartPointsFromOneReadingUseSameX();
PmSeriesRemainAligned();
VocAndNoxRemainAligned();
HumidityTemperatureAndCo2ReceivePoints();
DistanceReceivesNoChartPoint();
CollectionCountDoesNotExceedMaximum();
OldestPointsAreRemovedCorrectly();
HiddenSeriesDataContinuesAccumulating();
RestoringSeriesPreservesHistory();
DisconnectDoesNotClearHistory();
ReconnectDoesNotDuplicateSubscriptions();
YAxisMinimumIsZero();
ChartCollectionsUseViewModelUpdatePath();

Console.WriteLine("SensorPacketDecoder and direct chart-data tests passed.");

void NormalPacketDecodesAllFields()
{
    var packet = MakePacket(
        sequence: 42,
        pm1: 123,
        pm25: 456,
        pm4: 789,
        pm10: 1001,
        humidity: 5050,
        temperature: 4620,
        nox: 120,
        voc: 345,
        co2: 800,
        distanceRaw: 0);

    var reading = decoder.Decode(packet);

    Equal((ushort)42, reading.SequenceNumber, "sequence");
    CloseNullable(12.3, reading.Pm1, "pm1");
    CloseNullable(45.6, reading.Pm25, "pm25");
    CloseNullable(78.9, reading.Pm4, "pm4");
    CloseNullable(100.1, reading.Pm10, "pm10");
    CloseNullable(50.5, reading.Humidity, "humidity");
    CloseNullable(23.1, reading.Temperature, "temperature");
    CloseNullable(12.0, reading.Nox, "nox");
    CloseNullable(34.5, reading.Voc, "voc");
    Equal((ushort)800, reading.Co2, "co2");
    CloseRequired(0.0, reading.DistanceMetres, "distance");
}

void FirstByte03IsSequenceLowByte()
{
    var packet = MakePacket(sequence: 3);
    Equal((byte)0x03, packet[0], "packet[0]");
    Equal((ushort)3, decoder.Decode(packet).SequenceNumber, "sequence 3");
}

void NegativeTemperatureDecodes()
{
    var packet = MakePacket(temperature: -210);
    CloseNullable(-1.05, decoder.Decode(packet).Temperature, "negative temperature");
}

void DistanceZeroDecodesToZeroMetres()
{
    CloseRequired(0.0, decoder.Decode(MakePacket(distanceRaw: 0)).DistanceMetres, "zero distance");
}

void FutureDistance1250DecodesToOnePoint25Metres()
{
    CloseRequired(1.25, decoder.Decode(MakePacket(distanceRaw: 1250)).DistanceMetres, "future distance");
}

void Legacy20BytePacketIsRejected()
{
    Throws<SensorPacketFormatException>(() => decoder.Decode(MakePacket()[..20]), "legacy 20-byte packet");
}

void Correct22BytePacketIsAccepted()
{
    _ = decoder.Decode(MakePacket());
}

void MultipleSequentialPacketsDecode()
{
    for (ushort sequence = 0; sequence < 5; sequence++)
    {
        Equal(sequence, decoder.Decode(MakePacket(sequence: sequence)).SequenceNumber, $"sequence {sequence}");
    }
}

void OneReadingAddsOnePointToAllChartSeries()
{
    var model = CreateModel();
    model.AddChartReadingForTest(MakeReading());

    Equal(1, model.Pm1Values.Count, "pm1 count");
    Equal(1, model.Pm25Values.Count, "pm25 count");
    Equal(1, model.Pm4Values.Count, "pm4 count");
    Equal(1, model.Pm10Values.Count, "pm10 count");
    Equal(1, model.VocValues.Count, "voc count");
    Equal(1, model.NoxValues.Count, "nox count");
    Equal(1, model.HumidityValues.Count, "humidity count");
    Equal(1, model.TemperatureValues.Count, "temperature count");
    Equal(1, model.Co2Values.Count, "co2 count");
    CloseRequired(1.0, model.Pm1Values[0].Y ?? double.NaN, "pm1 chart value");
}

void AllChartPointsFromOneReadingUseSameX()
{
    var model = CreateModel();
    model.AddChartReadingForTest(MakeReading(timestamp: DateTimeOffset.Parse("2026-07-14T00:00:05Z")));

    var x = model.Pm1Values[0].X;
    Equal(x, model.Pm25Values[0].X, "pm25 x");
    Equal(x, model.Pm4Values[0].X, "pm4 x");
    Equal(x, model.Pm10Values[0].X, "pm10 x");
    Equal(x, model.VocValues[0].X, "voc x");
    Equal(x, model.NoxValues[0].X, "nox x");
    Equal(x, model.HumidityValues[0].X, "humidity x");
    Equal(x, model.TemperatureValues[0].X, "temperature x");
    Equal(x, model.Co2Values[0].X, "co2 x");
}

void PmSeriesRemainAligned()
{
    var model = CreateModel();
    AddManyReadings(model, 5);

    Equal(5, model.Pm1Values.Count, "pm1 aligned count");
    Equal(model.Pm1Values.Count, model.Pm25Values.Count, "pm25 aligned count");
    Equal(model.Pm1Values.Count, model.Pm4Values.Count, "pm4 aligned count");
    Equal(model.Pm1Values.Count, model.Pm10Values.Count, "pm10 aligned count");
}

void VocAndNoxRemainAligned()
{
    var model = CreateModel();
    AddManyReadings(model, 5);

    Equal(model.VocValues.Count, model.NoxValues.Count, "voc nox aligned count");
    Equal(model.VocValues[4].X, model.NoxValues[4].X, "voc nox aligned x");
}

void HumidityTemperatureAndCo2ReceivePoints()
{
    var model = CreateModel();
    model.AddChartReadingForTest(MakeReading());

    Equal(1, model.HumidityValues.Count, "humidity point");
    Equal(1, model.TemperatureValues.Count, "temperature point");
    Equal(1, model.Co2Values.Count, "co2 point");
}

void DistanceReceivesNoChartPoint()
{
    var model = CreateModel();
    model.AddChartReadingForTest(MakeReading());

    var allSeries = model.PmSeries
        .Concat(model.VocNoxSeries)
        .Concat(model.HumiditySeries)
        .Concat(model.TemperatureSeries)
        .Concat(model.Co2Series);
    Equal(false, allSeries.Any(series => string.Equals(series.Name, "Distance", StringComparison.OrdinalIgnoreCase)), "distance series absent");
}

void CollectionCountDoesNotExceedMaximum()
{
    var model = CreateModel();
    AddManyReadings(model, MainPageModel.ChartMaximumPoints + 5);

    Equal(MainPageModel.ChartMaximumPoints, model.Pm1Values.Count, "pm1 rolling count");
    Equal(MainPageModel.ChartMaximumPoints, model.Co2Values.Count, "co2 rolling count");
}

void OldestPointsAreRemovedCorrectly()
{
    var model = CreateModel();
    AddManyReadings(model, MainPageModel.ChartMaximumPoints + 5);

    Equal(5.0, model.Pm1Values[0].X ?? double.NaN, "oldest x after trim");
    Equal(304.0, model.Pm1Values[^1].X ?? double.NaN, "newest x after trim");
}

void HiddenSeriesDataContinuesAccumulating()
{
    var model = CreateModel();
    model.IsPm25Visible = false;
    AddManyReadings(model, 3);

    Equal(false, model.IsPm25Visible, "pm25 hidden");
    Equal(3, model.Pm25Values.Count, "hidden pm25 retained count");
}

void RestoringSeriesPreservesHistory()
{
    var model = CreateModel();
    model.IsVocVisible = false;
    AddManyReadings(model, 3);
    model.IsVocVisible = true;

    Equal(true, model.IsVocVisible, "voc restored");
    Equal(3, model.VocValues.Count, "voc restored history");
}

void DisconnectDoesNotClearHistory()
{
    var bluetooth = new FakeBluetoothSensorService();
    var model = new MainPageModel(bluetooth);
    model.AddChartReadingForTest(MakeReading());

    bluetooth.RaiseUnexpectedDisconnect();

    Equal(1, model.Pm1Values.Count, "history retained after disconnect");
}

void ReconnectDoesNotDuplicateSubscriptions()
{
    var bluetooth = new FakeBluetoothSensorService();
    _ = new MainPageModel(bluetooth);

    bluetooth.RaiseUnexpectedDisconnect();

    Equal(1, bluetooth.ReadingReceivedSubscriberCount, "reading subscription count");
}

void YAxisMinimumIsZero()
{
    var model = CreateModel();
    Equal(0.0, model.PmYAxes[0].MinLimit ?? double.NaN, "pm y min");
    Equal(null, model.PmYAxes[0].MaxLimit, "pm y max automatic");
    Equal(0.0, model.Co2YAxes[0].MinLimit ?? double.NaN, "co2 y min");
    Equal(false, ReferenceEquals(model.PmYAxes[0], model.Co2YAxes[0]), "chart axes are not shared");
}

void ChartCollectionsUseViewModelUpdatePath()
{
    var model = CreateModel();
    model.AddChartReadingForTest(MakeReading());

    Equal(true, model.Pm1ValuesReferenceMatches, "pm1 values reference");
    Equal(true, model.Pm1SeriesReferenceMatches, "pm1 series reference");
}

static MainPageModel CreateModel()
    => new(new FakeBluetoothSensorService());

static void AddManyReadings(MainPageModel model, int count)
{
    var start = DateTimeOffset.Parse("2026-07-14T00:00:00Z");
    for (ushort sequence = 0; sequence < count; sequence++)
    {
        model.AddChartReadingForTest(MakeReading(timestamp: start.AddSeconds(sequence), sequence: sequence));
    }
}

static SensorReading MakeReading(DateTimeOffset? timestamp = null, ushort sequence = 1, double? pm1 = 1.0, ushort? co2 = 800)
    => new(
        timestamp ?? DateTimeOffset.Parse("2026-07-14T00:00:00Z"),
        sequence,
        pm1,
        2.0,
        3.0,
        4.0,
        50.0,
        24.0,
        6.0,
        7.0,
        co2,
        1.25);

static byte[] MakePacket(
    ushort sequence = 1,
    ushort pm1 = 10,
    ushort pm25 = 20,
    ushort pm4 = 30,
    ushort pm10 = 40,
    short humidity = 5000,
    short temperature = 4200,
    short nox = 50,
    short voc = 60,
    ushort co2 = 700,
    ushort distanceRaw = 0)
{
    var packet = new byte[SensorPacketDecoder.PacketLength];
    WriteU16(packet, 0, sequence);
    WriteU16(packet, 2, pm1);
    WriteU16(packet, 4, pm25);
    WriteU16(packet, 6, pm4);
    WriteU16(packet, 8, pm10);
    WriteI16(packet, 10, humidity);
    WriteI16(packet, 12, temperature);
    WriteI16(packet, 14, nox);
    WriteI16(packet, 16, voc);
    WriteU16(packet, 18, co2);
    WriteU16(packet, 20, distanceRaw);
    return packet;
}

static void WriteU16(byte[] packet, int offset, ushort value)
    => BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(offset, 2), value);

static void WriteI16(byte[] packet, int offset, short value)
    => BinaryPrimitives.WriteInt16LittleEndian(packet.AsSpan(offset, 2), value);

static void Equal<T>(T expected, T actual, string name)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"{name}: expected {expected}, received {actual}.");
    }
}

static void CloseNullable(double expected, double? actual, string name)
{
    if (!actual.HasValue || Math.Abs(expected - actual.Value) > 0.000001)
    {
        throw new InvalidOperationException($"{name}: expected {expected}, received {actual?.ToString() ?? "<null>"}.");
    }
}

static void CloseRequired(double expected, double actual, string name)
{
    if (Math.Abs(expected - actual) > 0.000001)
    {
        throw new InvalidOperationException($"{name}: expected {expected}, received {actual}.");
    }
}

static void Throws<TException>(Action action, string name)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"{name}: expected {typeof(TException).Name}.");
}

internal sealed class FakeBluetoothSensorService : IBluetoothSensorService
{
    private EventHandler<SensorReading>? _readingReceived;

    public event EventHandler<DiscoveredSensorDevice>? DeviceDiscovered;

    public event EventHandler<SensorReading>? ReadingReceived
    {
        add => _readingReceived += value;
        remove => _readingReceived -= value;
    }

    public event EventHandler<string>? DiagnosticMessage;

    public event EventHandler? UnexpectedlyDisconnected;

    public int ReadingReceivedSubscriberCount
        => _readingReceived?.GetInvocationList().Length ?? 0;

    public BluetoothConnectionState State { get; private set; } = BluetoothConnectionState.Idle;

    public SensorReading? LatestReading { get; private set; }

    public ulong TotalPacketsReceived { get; private set; }

    public ulong MalformedPacketsReceived { get; private set; }

    public ulong SequenceGapsDetected { get; private set; }

    public DateTimeOffset? LastPacketTime { get; private set; }

    public Task<IReadOnlyList<DiscoveredSensorDevice>> ScanAsync(string? filter, TimeSpan timeout, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<DiscoveredSensorDevice>>([]);

    public Task StopScanAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task ConnectAsync(string deviceId, CancellationToken cancellationToken)
    {
        State = BluetoothConnectionState.ReceivingData;
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(bool userInitiated, CancellationToken cancellationToken)
    {
        State = BluetoothConnectionState.Disconnected;
        return Task.CompletedTask;
    }

    public Task ReconnectAsync(CancellationToken cancellationToken)
    {
        State = BluetoothConnectionState.ReceivingData;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
        => ValueTask.CompletedTask;

    public void RaiseUnexpectedDisconnect()
        => UnexpectedlyDisconnected?.Invoke(this, EventArgs.Empty);

    public void RaiseReading(SensorReading reading)
    {
        LatestReading = reading;
        TotalPacketsReceived++;
        LastPacketTime = reading.Timestamp;
        _readingReceived?.Invoke(this, reading);
    }

    public void RaiseDeviceDiscovered(DiscoveredSensorDevice device)
        => DeviceDiscovered?.Invoke(this, device);

    public void RaiseDiagnostic(string message)
        => DiagnosticMessage?.Invoke(this, message);
}
