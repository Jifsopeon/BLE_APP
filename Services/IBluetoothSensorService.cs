using BLE_APP.Models;

namespace BLE_APP.Services;

public interface IBluetoothSensorService : IAsyncDisposable
{
    event EventHandler<DiscoveredSensorDevice>? DeviceDiscovered;

    event EventHandler<SensorReading>? ReadingReceived;

    event EventHandler<string>? DiagnosticMessage;

    event EventHandler? UnexpectedlyDisconnected;

    BluetoothConnectionState State { get; }

    SensorReading? LatestReading { get; }

    ulong TotalPacketsReceived { get; }

    ulong MalformedPacketsReceived { get; }

    ulong SequenceGapsDetected { get; }

    DateTimeOffset? LastPacketTime { get; }

    bool CanSetManualLabel { get; }

    Task<IReadOnlyList<DiscoveredSensorDevice>> ScanAsync(string? filter, TimeSpan timeout, CancellationToken cancellationToken);

    Task StopScanAsync(CancellationToken cancellationToken);

    Task ConnectAsync(string deviceId, CancellationToken cancellationToken);

    Task DisconnectAsync(bool userInitiated, CancellationToken cancellationToken);

    Task ReconnectAsync(CancellationToken cancellationToken);

    Task SetManualLabelAsync(ManualLabelState label, CancellationToken cancellationToken);
}
