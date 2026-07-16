namespace BLE_APP.Models;

public sealed record SensorReading(
    DateTimeOffset Timestamp,
    ushort SequenceNumber,
    double? Pm1,
    double? Pm25,
    double? Pm4,
    double? Pm10,
    double? Humidity,
    double? Temperature,
    double? Nox,
    double? Voc,
    ushort? Co2,
    double DistanceMetres,
    ManualLabelState ManualLabel,
    byte ManualLabelRaw);
