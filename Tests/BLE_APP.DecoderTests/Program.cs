using System.Buffers.Binary;
using BLE_APP.Models;
using BLE_APP.PageModels;
using BLE_APP.Services;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;

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
CsvRowsFollowOneSecondNotifications();
LogFilenameParsingIgnoresMalformedNames();
LogFilenameAllocationHandlesGapsAndCollisions();
LogFilenameAllocationUsesSelectedFolderNamesDirectly();
LogFilenameAllocationHandlesSelectedFolderNamedLog();
OneReadingAddsOnePointToAllChartSeries();
AllChartPointsFromOneReadingUseSameX();
PmSeriesRemainAligned();
VocAndNoxRemainAligned();
HumidityTemperatureAndCo2ReceivePoints();
DistanceReceivesNoChartPoint();
InitialChartXAxisRangeIsFiveMinutes();
ChartWindowRetainsInclusiveFiveMinuteBoundary();
ChartWindowPrunesPointsOlderThanFiveMinutes();
ChartWindowRetainsOneSecondSamplesForFiveMinutes();
ChartWindowUsesElapsedTimeNotPointCount();
AllChartAxesShareLatestFiveMinuteWindow();
HiddenSeriesDataContinuesAccumulating();
HiddenSeriesIsPrunedToChartWindow();
RestoringSeriesPreservesHistory();
ResetChartSessionClearsPointsAndTimeWindow();
DisconnectDoesNotClearHistory();
ReconnectDoesNotDuplicateSubscriptions();
YAxisMinimumIsZero();
ChartCollectionsUseViewModelUpdatePath();
ChartSeriesColorPropertiesMatchStrokeColors();
ChartSeriesColorsStayStableWhenVisibilityChanges();
ManualLabelConfirmationTimeoutAllowsOneSecondSampling();
await ManualLabelUiCommandRequestsSmoking();
ExactNameMatchUsesOrdinalIgnoreCaseAndTrim();
ExactNameMatchIgnoresEmptySearch();
ExactNameMatchOrdersAheadOfPartialAndIdMatches();
MultipleExactNameMatchesStayGroupedWithStableOrder();
DuplicateAdvertisementUpdatesWithoutDuplicateRows();
RssiUpdatePreservesSelection();
SearchTextChangeReordersDeviceList();

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

void CsvRowsFollowOneSecondNotifications()
{
    var start = DateTimeOffset.Parse("2026-07-14T08:05:09+08:00");
    var rows = Enumerable.Range(0, 3)
        .Select(index => SensorCsvFormatter.FormatRow(index + 1, start, MakeReading(timestamp: start.AddSeconds(index), sequence: (ushort)index)))
        .ToArray();

    Equal(3, rows.Length, "csv rows for one-second notifications");
    Equal("08:05:09", rows[0].Split(',')[2], "csv first one-second row time");
    Equal("08:05:10", rows[1].Split(',')[2], "csv second one-second row time");
    Equal("08:05:11", rows[2].Split(',')[2], "csv third one-second row time");
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

void LogFilenameAllocationUsesSelectedFolderNamesDirectly()
{
    var selectedFolderNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Log0001.csv",
        "Log0002.csv",
        "Log0005.csv",
        "OtherFile.txt",
        "Log",
        "Log99.csv"
    };

    Equal("Log0006.csv", SensorLogFileNameAllocator.AllocateNextFileName(selectedFolderNames), "selected folder direct allocation");
}

void LogFilenameAllocationHandlesSelectedFolderNamedLog()
{
    var explicitlySelectedLogFolderNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Log0009.csv"
    };

    Equal("Log0010.csv", SensorLogFileNameAllocator.AllocateNextFileName(explicitlySelectedLogFolderNames), "selected folder named Log allocation");
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

void InitialChartXAxisRangeIsFiveMinutes()
{
    var model = CreateModel();

    Equal(0.0, model.PmXAxes[0].MinLimit ?? double.NaN, "initial pm x min");
    Equal(MainPageModel.ChartWindowSeconds, model.PmXAxes[0].MaxLimit ?? double.NaN, "initial pm x max");
    Equal(0.0, model.Co2XAxes[0].MinLimit ?? double.NaN, "initial co2 x min");
    Equal(MainPageModel.ChartWindowSeconds, model.Co2XAxes[0].MaxLimit ?? double.NaN, "initial co2 x max");
}

void ChartWindowRetainsInclusiveFiveMinuteBoundary()
{
    var model = CreateModel();
    AddReadingsAtElapsedSeconds(model, 0, 5, 300);

    Equal(61, model.Pm1Values.Count, "inclusive window point count at 300s");
    Equal(0.0, model.Pm1Values[0].X ?? double.NaN, "inclusive first x");
    Equal(300.0, model.Pm1Values[^1].X ?? double.NaN, "inclusive latest x");
    Equal(0.0, model.PmXAxes[0].MinLimit ?? double.NaN, "inclusive axis min");
    Equal(300.0, model.PmXAxes[0].MaxLimit ?? double.NaN, "inclusive axis max");
}

void ChartWindowPrunesPointsOlderThanFiveMinutes()
{
    var model = CreateModel();
    AddReadingsAtElapsedSeconds(model, 0, 5, 305);

    Equal(61, model.Pm1Values.Count, "pruned window point count at 305s");
    Equal(5.0, model.Pm1Values[0].X ?? double.NaN, "oldest x after time trim");
    Equal(305.0, model.Pm1Values[^1].X ?? double.NaN, "newest x after time trim");
    Equal(5.0, model.PmXAxes[0].MinLimit ?? double.NaN, "scrolled axis min");
    Equal(305.0, model.PmXAxes[0].MaxLimit ?? double.NaN, "scrolled axis max");
}

void ChartWindowRetainsOneSecondSamplesForFiveMinutes()
{
    var model = CreateModel();
    AddReadingsAtElapsedSeconds(model, 0, 1, 300);

    Equal(301, model.Pm1Values.Count, "one-second five-minute point count");
    Equal(0.0, model.Pm1Values[0].X ?? double.NaN, "one-second first x at 300s");
    Equal(300.0, model.Pm1Values[^1].X ?? double.NaN, "one-second latest x at 300s");

    model.AddChartReadingForTest(MakeReading(timestamp: DateTimeOffset.Parse("2026-07-14T00:05:01Z"), sequence: 301));

    Equal(301, model.Pm1Values.Count, "one-second scrolled five-minute point count");
    Equal(1.0, model.Pm1Values[0].X ?? double.NaN, "one-second first retained x");
    Equal(301.0, model.Pm1Values[^1].X ?? double.NaN, "one-second newest x");
    Equal(1.0, model.PmXAxes[0].MinLimit ?? double.NaN, "one-second axis min");
    Equal(301.0, model.PmXAxes[0].MaxLimit ?? double.NaN, "one-second axis max");
}

void ChartWindowUsesElapsedTimeNotPointCount()
{
    var model = CreateModel();
    AddReadingsAtElapsedSeconds(model, 0, 120, 600);

    Equal(3, model.Pm1Values.Count, "delayed sample count");
    Equal(360.0, model.Pm1Values[0].X ?? double.NaN, "delayed first retained x");
    Equal(600.0, model.Pm1Values[^1].X ?? double.NaN, "delayed latest x");
}

void AllChartAxesShareLatestFiveMinuteWindow()
{
    var model = CreateModel();
    AddReadingsAtElapsedSeconds(model, 0, 5, 425);

    Equal(125.0, model.PmXAxes[0].MinLimit ?? double.NaN, "pm x min synced");
    Equal(425.0, model.PmXAxes[0].MaxLimit ?? double.NaN, "pm x max synced");
    Equal(model.PmXAxes[0].MinLimit, model.VocNoxXAxes[0].MinLimit, "voc nox x min synced");
    Equal(model.PmXAxes[0].MaxLimit, model.VocNoxXAxes[0].MaxLimit, "voc nox x max synced");
    Equal(model.PmXAxes[0].MinLimit, model.HumidityXAxes[0].MinLimit, "humidity x min synced");
    Equal(model.PmXAxes[0].MaxLimit, model.HumidityXAxes[0].MaxLimit, "humidity x max synced");
    Equal(model.PmXAxes[0].MinLimit, model.TemperatureXAxes[0].MinLimit, "temperature x min synced");
    Equal(model.PmXAxes[0].MaxLimit, model.TemperatureXAxes[0].MaxLimit, "temperature x max synced");
    Equal(model.PmXAxes[0].MinLimit, model.Co2XAxes[0].MinLimit, "co2 x min synced");
    Equal(model.PmXAxes[0].MaxLimit, model.Co2XAxes[0].MaxLimit, "co2 x max synced");
}

void HiddenSeriesDataContinuesAccumulating()
{
    var model = CreateModel();
    model.IsPm25Visible = false;
    AddManyReadings(model, 3);

    Equal(false, model.IsPm25Visible, "pm25 hidden");
    Equal(3, model.Pm25Values.Count, "hidden pm25 retained count");
}

void HiddenSeriesIsPrunedToChartWindow()
{
    var model = CreateModel();
    model.IsPm25Visible = false;
    AddReadingsAtElapsedSeconds(model, 0, 5, 305);

    Equal(false, model.IsPm25Visible, "pm25 hidden for pruning");
    Equal(61, model.Pm25Values.Count, "hidden pm25 pruned count");
    Equal(5.0, model.Pm25Values[0].X ?? double.NaN, "hidden pm25 first retained x");
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

void ResetChartSessionClearsPointsAndTimeWindow()
{
    var model = CreateModel();
    AddReadingsAtElapsedSeconds(model, 0, 5, 305);

    model.ResetChartSessionForTest();

    Equal(0, model.Pm1Values.Count, "reset clears pm1");
    Equal(0.0, model.PmXAxes[0].MinLimit ?? double.NaN, "reset x min");
    Equal(MainPageModel.ChartWindowSeconds, model.PmXAxes[0].MaxLimit ?? double.NaN, "reset x max");

    model.AddChartReadingForTest(MakeReading(timestamp: DateTimeOffset.Parse("2026-07-14T01:00:00Z")));
    Equal(0.0, model.Pm1Values[0].X ?? double.NaN, "reset restarts elapsed time");
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

void ChartSeriesColorPropertiesMatchStrokeColors()
{
    var model = CreateModel();

    Equal(model.Pm1SeriesColor, StrokeHex(model.PmSeries[0]), "pm1 marker color");
    Equal(model.Pm25SeriesColor, StrokeHex(model.PmSeries[1]), "pm25 marker color");
    Equal(model.Pm4SeriesColor, StrokeHex(model.PmSeries[2]), "pm4 marker color");
    Equal(model.Pm10SeriesColor, StrokeHex(model.PmSeries[3]), "pm10 marker color");
    Equal(model.VocSeriesColor, StrokeHex(model.VocNoxSeries[0]), "voc marker color");
    Equal(model.NoxSeriesColor, StrokeHex(model.VocNoxSeries[1]), "nox marker color");
    Equal(model.HumiditySeriesColor, StrokeHex(model.HumiditySeries[0]), "humidity marker color");
    Equal(model.TemperatureSeriesColor, StrokeHex(model.TemperatureSeries[0]), "temperature marker color");
    Equal(model.Co2SeriesColor, StrokeHex(model.Co2Series[0]), "co2 marker color");
}

void ChartSeriesColorsStayStableWhenVisibilityChanges()
{
    var model = CreateModel();
    var pm25Color = model.Pm25SeriesColor;
    var vocColor = model.VocSeriesColor;
    var noxColor = model.NoxSeriesColor;

    model.TogglePm25Command.Execute(null);
    model.ToggleVocCommand.Execute(null);
    model.ToggleNoxCommand.Execute(null);
    Equal(false, model.IsPm25Visible, "pm25 toggled off");
    Equal(false, model.IsVocVisible, "voc toggled off");
    Equal(false, model.IsNoxVisible, "nox toggled off");

    Equal(pm25Color, model.Pm25SeriesColor, "pm25 hidden marker color");
    Equal(pm25Color, StrokeHex(model.PmSeries[1]), "pm25 hidden stroke color");
    Equal(vocColor, model.VocSeriesColor, "voc hidden marker color");
    Equal(vocColor, StrokeHex(model.VocNoxSeries[0]), "voc hidden stroke color");
    Equal(noxColor, model.NoxSeriesColor, "nox hidden marker color");
    Equal(noxColor, StrokeHex(model.VocNoxSeries[1]), "nox hidden stroke color");

    model.TogglePm25Command.Execute(null);
    model.ToggleVocCommand.Execute(null);
    model.ToggleNoxCommand.Execute(null);
    Equal(true, model.IsPm25Visible, "pm25 toggled on");
    Equal(true, model.IsVocVisible, "voc toggled on");
    Equal(true, model.IsNoxVisible, "nox toggled on");

    Equal(pm25Color, model.Pm25SeriesColor, "pm25 restored marker color");
    Equal(pm25Color, StrokeHex(model.PmSeries[1]), "pm25 restored stroke color");
    Equal(vocColor, model.VocSeriesColor, "voc restored marker color");
    Equal(vocColor, StrokeHex(model.VocNoxSeries[0]), "voc restored stroke color");
    Equal(noxColor, model.NoxSeriesColor, "nox restored marker color");
    Equal(noxColor, StrokeHex(model.VocNoxSeries[1]), "nox restored stroke color");
}

void ManualLabelConfirmationTimeoutAllowsOneSecondSampling()
{
    Equal(true, MainPageModel.ManualLabelConfirmationTimeoutSeconds >= 3, "manual label timeout covers multiple one-second samples");
}

async Task ManualLabelUiCommandRequestsSmoking()
{
    var bluetooth = new FakeBluetoothSensorService { CanSetManualLabel = true };
    var model = new MainPageModel(bluetooth, new FakeSensorLogService());

    await model.SetSmokingCommand.ExecuteAsync(null);

    Equal(ManualLabelState.Smoking, bluetooth.LastRequestedManualLabel, "ui smoking command");
}

void ExactNameMatchUsesOrdinalIgnoreCaseAndTrim()
{
    Equal(true, MainPageModel.IsExactNameMatch("PSE84-IAQ", "pse84-iaq"), "exact lower-case name");
    Equal(true, MainPageModel.IsExactNameMatch(" PSE84-IAQ ", " Pse84-Iaq "), "exact trimmed mixed-case name");
    Equal(false, MainPageModel.IsExactNameMatch("PSE84", "PSE84-IAQ"), "partial name is not exact");
}

void ExactNameMatchIgnoresEmptySearch()
{
    Equal(false, MainPageModel.IsExactNameMatch(string.Empty, string.Empty), "empty search no exact match");
    Equal(false, MainPageModel.IsExactNameMatch("   ", "(unnamed)"), "whitespace search no exact match");
    Equal(false, MainPageModel.IsExactNameMatch(null, "PSE84-IAQ"), "null search no exact match");
}

void ExactNameMatchOrdersAheadOfPartialAndIdMatches()
{
    var model = CreateModel();
    model.SearchText = "PSE84-IAQ";

    model.AddDiscoveredDeviceForTest(MakeDevice("id-partial", "PSE84-IAQ-TEST"));
    model.AddDiscoveredDeviceForTest(MakeDevice("PSE84-IAQ", "ID match only"));
    model.AddDiscoveredDeviceForTest(MakeDevice("id-exact", "pse84-iaq"));

    Equal("id-exact", model.Devices[0].Id, "exact name first");
    Equal("id-partial", model.Devices[1].Id, "partial remains below exact");
    Equal("PSE84-IAQ", model.Devices[2].Id, "id match does not outrank exact name");
}

void MultipleExactNameMatchesStayGroupedWithStableOrder()
{
    var model = CreateModel();
    model.SearchText = "PSE84-IAQ";

    model.AddDiscoveredDeviceForTest(MakeDevice("partial", "PSE84-IAQ-TEST"));
    model.AddDiscoveredDeviceForTest(MakeDevice("exact-1", "PSE84-IAQ"));
    model.AddDiscoveredDeviceForTest(MakeDevice("exact-2", " pse84-iaq "));

    Equal("exact-1", model.Devices[0].Id, "first exact remains first exact");
    Equal("exact-2", model.Devices[1].Id, "second exact remains second exact");
    Equal("partial", model.Devices[2].Id, "partial remains after exact group");
}

void DuplicateAdvertisementUpdatesWithoutDuplicateRows()
{
    var model = CreateModel();
    model.SearchText = "PSE84-IAQ";

    model.AddDiscoveredDeviceForTest(MakeDevice("same-id", "Other", rssi: -70));
    model.AddDiscoveredDeviceForTest(MakeDevice("same-id", "pse84-iaq", rssi: -40));

    Equal(1, model.Devices.Count, "duplicate advertisement count");
    Equal("pse84-iaq", model.Devices[0].Name, "duplicate advertisement updates name");
    Equal(-40, model.Devices[0].SignalStrengthDbm, "duplicate advertisement updates rssi");
}

void RssiUpdatePreservesSelection()
{
    var model = CreateModel();
    model.SearchText = "PSE84-IAQ";

    model.AddDiscoveredDeviceForTest(MakeDevice("selected", "Other", rssi: -80));
    model.AddDiscoveredDeviceForTest(MakeDevice("exact", "PSE84-IAQ", rssi: -60));
    model.SelectedDevice = model.Devices.First(device => device.Id == "selected");

    model.AddDiscoveredDeviceForTest(MakeDevice("selected", "Other", rssi: -30));

    Equal("selected", model.SelectedDevice?.Id, "selection preserved after rssi update");
    Equal(2, model.Devices.Count, "rssi update does not duplicate selected device");
}

void SearchTextChangeReordersDeviceList()
{
    var model = CreateModel();
    model.SearchText = "Other";

    model.AddDiscoveredDeviceForTest(MakeDevice("other", "Other"));
    model.AddDiscoveredDeviceForTest(MakeDevice("target", "PSE84-IAQ"));

    Equal("other", model.Devices[0].Id, "initial exact first");

    model.SearchText = " pse84-iaq ";

    Equal("target", model.Devices[0].Id, "search change promotes new exact match");
    Equal("other", model.Devices[1].Id, "previous exact demoted stably");
}

static MainPageModel CreateModel()
    => new(new FakeBluetoothSensorService(), new FakeSensorLogService());

static DiscoveredSensorDevice MakeDevice(string id, string name, int rssi = -60, bool advertisesExpectedService = false)
    => new(id, name, rssi, advertisesExpectedService, DateTimeOffset.Parse("2026-07-14T00:00:00Z"));

static string StrokeHex(ISeries series)
{
    if (series is not LineSeries<ObservablePoint> lineSeries || lineSeries.Stroke is not SolidColorPaint stroke)
    {
        throw new InvalidOperationException($"{series.Name}: expected a line series with a solid color stroke.");
    }

    var color = stroke.Color;
    return $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}";
}

static void AddManyReadings(MainPageModel model, int count)
{
    var start = DateTimeOffset.Parse("2026-07-14T00:00:00Z");
    for (ushort sequence = 0; sequence < count; sequence++)
    {
        model.AddChartReadingForTest(MakeReading(timestamp: start.AddSeconds(sequence), sequence: sequence));
    }
}

static void AddReadingsAtElapsedSeconds(MainPageModel model, int startSeconds, int stepSeconds, int endSeconds)
{
    var start = DateTimeOffset.Parse("2026-07-14T00:00:00Z");
    ushort sequence = 0;
    for (var elapsedSeconds = startSeconds; elapsedSeconds <= endSeconds; elapsedSeconds += stepSeconds)
    {
        model.AddChartReadingForTest(MakeReading(timestamp: start.AddSeconds(elapsedSeconds), sequence: sequence++));
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
