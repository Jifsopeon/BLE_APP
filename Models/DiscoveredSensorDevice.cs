namespace BLE_APP.Models;

public sealed record DiscoveredSensorDevice(
    string Id,
    string Name,
    int SignalStrengthDbm,
    bool AdvertisesExpectedService,
    DateTimeOffset LastSeen);
