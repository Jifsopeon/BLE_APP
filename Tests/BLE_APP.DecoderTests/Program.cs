using System.Buffers.Binary;
using BLE_APP.Models;
using BLE_APP.PageModels;
using BLE_APP.Services;

var decoder = new SensorPacketDecoder();

NormalPacketDecodesAllFields();
ActualFirmwarePacketBytesDecode();
FirstByte03IsSequenceLowByte();
NegativeTemperatureDecodes();
DistanceZeroDecodesToZeroMetres();
FutureDistance1250DecodesToOnePoint25Metres();
Legacy20BytePacketIsRejected();
Old22BytePacketIsRejected();
ManualLabelPacketSmokingDecodes();
ManualLabelPacketNoSmokingDecodes();
UnknownManualLabelRawIsRejected();
MultipleSequentialPacketsDecode();
CsvHeaderMatchesExpectedOrder();
CsvRowFormatsManualLabelAndRawValue();
CsvRowUsesInvariantFormattingAndOffsetTimestamp();
CsvRowTimeUsesSecondsWithoutMilliseconds();
LogFilenameParsingIgnoresMalformedNames();
LogFilenameAllocationHandlesGapsAndCollisions();
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
await ManualLabelUiCommandRequestsSmoking();

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

void ActualFirmwarePacketBytesDecode()
{
    byte[] packet =
    [
        0x2A, 0x00,
        0x7B, 0x00,
        0xC8, 0x01,
        0x15, 0x03,
        0xE9, 0x03,
        0xBA, 0x13,
        0x0C, 0x12,
        0x78, 0x00,
        0x59, 0x01,
        0x20, 0x03,
        0xE2, 0x04,
        0x01
    ];

    var reading = decoder.Decode(packet);

    Equal((ushort)42, reading.SequenceNumber, "firmware sequence");
    CloseNullable(12.3, reading.Pm1, "firmware pm1");
    CloseNullable(45.6, reading.Pm25, "firmware pm25");
    CloseNullable(78.9, reading.Pm4, "firmware pm4");
    CloseNullable(100.1, reading.Pm10, "firmware pm10");
    CloseNullable(50.5, reading.Humidity, "firmware humidity");
    CloseNullable(23.1, reading.Temperature, "firmware temperature");
    CloseNullable(12.0, reading.Nox, "firmware nox");
    CloseNullable(34.5, reading.Voc, "firmware voc");
    Equal((ushort)800, reading.Co2, "firmware co2");
    CloseRequired(1.25, reading.DistanceMetres, "firmware distance");
    Equal(ManualLabelState.Smoking, reading.ManualLabel, "firmware manual label");
    Equal((byte)1, reading.ManualLabelRaw, "firmware manual label raw");
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

void Old22BytePacketIsRejected()
{
    Throws<SensorPacketFormatException>(() => decoder.Decode(MakeLegacyPacket()), "old 22-byte packet");
}

void ManualLabelPacketSmokingDecodes()
{
    var reading = decoder.Decode(MakeManualPacket(SensorPacketProtocol.ManualLabelSmokingRaw));
    Equal(ManualLabelState.Smoking, reading.ManualLabel, "manual label smoking");
    Equal((byte)1, reading.ManualLabelRaw, "manual label smoking raw");
}

void ManualLabelPacketNoSmokingDecodes()
{
    var reading = decoder.Decode(MakeManualPacket(SensorPacketProtocol.ManualLabelNoSmokingRaw));
    Equal(ManualLabelState.NoSmoking, reading.ManualLabel, "manual label no smoking");
    Equal((byte)0, reading.ManualLabelRaw, "manual label no smoking raw");
}

void UnknownManualLabelRawIsRejected()
{
    Throws<SensorPacketFormatException>(() => decoder.Decode(MakeManualPacket(0x7F)), "unknown manual label raw");
}

void MultipleSequentialPacketsDecode()
{
    for (ushort sequence = 0; sequence < 5; sequence++)
    {
        Equal(sequence, decoder.Decode(MakePacket(sequence: sequence)).SequenceNumber, $"sequence {sequence}");
    }
}

void CsvHeaderMatchesExpectedOrder()
{
    Equal("Sample,Date,Time,TimestampISO8601,ElapsedSeconds,PM1_0,PM2_5,PM4_0,PM10,Humidity,Temperature,VOC,NOx,CO2,ManualLabel,ManualLabelRaw",
        SensorCsvFormatter.Header,
        "csv header");
}

void CsvRowFormatsManualLabelAndRawValue()
{
    var timestamp = DateTimeOffset.Parse("2026-07-14T08:30:15+08:00");
    var reading = MakeReading(timestamp: timestamp);
    var row = SensorCsvFormatter.FormatRow(1, timestamp.AddSeconds(-2), reading);

    Equal(true, row.Contains(",Smoking,1", StringComparison.Ordinal), "csv manual label");
}

void CsvRowUsesInvariantFormattingAndOffsetTimestamp()
{
    var timestamp = DateTimeOffset.Parse("2026-07-14T08:30:15.123+08:00");
    var row = SensorCsvFormatter.FormatRow(12, timestamp.AddSeconds(-1.5), MakeReading(timestamp: timestamp, pm1: 12.3));

    Equal(true, row.Contains("2026-07-14T08:30:15.1230000+08:00", StringComparison.Ordinal), "csv timestamp offset");
    Equal(true, row.Contains(",1.5,12.3,", StringComparison.Ordinal), "csv invariant decimals");
}

void CsvRowTimeUsesSecondsWithoutMilliseconds()
{
    var timestamp = DateTimeOffset.Parse("2026-07-14T08:05:09.123+08:00");
    var row = SensorCsvFormatter.FormatRow(1, timestamp.AddSeconds(-1), MakeReading(timestamp: timestamp));
    var columns = row.Split(',');

    Equal("08:05:09", columns[2], "csv time without milliseconds");
    Equal(false, columns[2].Contains('.', StringComparison.Ordinal), "csv time has no fractional seconds");
}

void LogFilenameParsingIgnoresMalformedNames()
{
    Equal(23, SensorLogFileNameAllocator.ParseLogNumber("Log0023.csv"), "parse log number");
    Equal(0, SensorLogFileNameAllocator.ParseLogNumber("Log23.csv"), "ignore short log number");
    Equal(0, SensorLogFileNameAllocator.ParseLogNumber("Other0023.csv"), "ignore unrelated log number");
}

void LogFilenameAllocationHandlesGapsAndCollisions()
{
    var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Log0001.csv",
        "Log0003.csv",
        "Log0004.csv"
    };

    var fileName = SensorLogFileNameAllocator.AllocateNextFileName(existing, lastAllocatedNumber: 3, out var allocatedNumber);
    Equal("Log0005.csv", fileName, "allocated filename");
    Equal(5, allocatedNumber, "allocated number");
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
    var model = new MainPageModel(bluetooth, new FakeSensorLogService());
    model.AddChartReadingForTest(MakeReading());

    bluetooth.RaiseUnexpectedDisconnect();

    Equal(1, model.Pm1Values.Count, "history retained after disconnect");
}

void ReconnectDoesNotDuplicateSubscriptions()
{
    var bluetooth = new FakeBluetoothSensorService();
    _ = new MainPageModel(bluetooth, new FakeSensorLogService());

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

async Task ManualLabelUiCommandRequestsSmoking()
{
    var bluetooth = new FakeBluetoothSensorService { CanSetManualLabel = true };
    var model = new MainPageModel(bluetooth, new FakeSensorLogService());

    await model.SetSmokingCommand.ExecuteAsync(null);

    Equal(ManualLabelState.Smoking, bluetooth.LastRequestedManualLabel, "ui smoking command");
}

static MainPageModel CreateModel()
    => new(new FakeBluetoothSensorService(), new FakeSensorLogService());

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
        1.25,
        ManualLabelState.Smoking,
        1);

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
    var packet = new byte[SensorPacketProtocol.PacketLength];
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
    packet[SensorPacketProtocol.ManualLabelOffset] = SensorPacketProtocol.ManualLabelSmokingRaw;
    return packet;
}

static byte[] MakeLegacyPacket()
{
    var packet = new byte[22];
    MakePacket().AsSpan(0, packet.Length).CopyTo(packet);
    return packet;
}

static byte[] MakeManualPacket(byte rawLabel)
{
    var packet = MakePacket();
    packet[SensorPacketProtocol.ManualLabelOffset] = rawLabel;
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

    public bool CanSetManualLabel { get; set; }

    public ManualLabelState? LastRequestedManualLabel { get; private set; }

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

    public Task SetManualLabelAsync(ManualLabelState label, CancellationToken cancellationToken)
    {
        LastRequestedManualLabel = label;
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

internal sealed class FakeSensorLogService : ISensorLogService
{
    public event EventHandler<string>? StatusChanged;

    public bool IsLogging { get; private set; }

    public string? CurrentLogName { get; private set; }

    public string StatusText { get; private set; } = "Waiting for data";

    public string LogFolderDisplayText { get; private set; } = "Not selected";

    public Task StartSessionAsync(SensorReading firstReading, long connectionGeneration, CancellationToken cancellationToken = default)
    {
        IsLogging = true;
        CurrentLogName = "Log0001.csv";
        StatusText = "Logging: Log0001.csv";
        StatusChanged?.Invoke(this, StatusText);
        return Task.CompletedTask;
    }

    public Task AppendAsync(SensorReading reading, long connectionGeneration, CancellationToken cancellationToken = default)
    {
        if (!IsLogging)
        {
            return StartSessionAsync(reading, connectionGeneration, cancellationToken);
        }

        return Task.CompletedTask;
    }

    public Task<LogSaveResult> StopAndSaveAsync(LogStopReason reason, long connectionGeneration, CancellationToken cancellationToken = default)
    {
        IsLogging = false;
        StatusText = CurrentLogName is null ? "Waiting for data" : $"Saved: {CurrentLogName}";
        StatusChanged?.Invoke(this, StatusText);
        return Task.FromResult(new LogSaveResult(CurrentLogName, null, CurrentLogName is not null, ErrorMessage: null));
    }

    public Task SelectPublicFolderAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
