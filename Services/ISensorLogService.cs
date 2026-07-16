using BLE_APP.Models;

namespace BLE_APP.Services;

public interface ISensorLogService
{
    event EventHandler<string>? StatusChanged;

    bool IsLogging { get; }

    string? CurrentLogName { get; }

    string StatusText { get; }

    string LogFolderDisplayText { get; }

    Task StartSessionAsync(SensorReading firstReading, long connectionGeneration, CancellationToken cancellationToken = default);

    Task AppendAsync(SensorReading reading, long connectionGeneration, CancellationToken cancellationToken = default);

    Task<LogSaveResult> StopAndSaveAsync(LogStopReason reason, long connectionGeneration, CancellationToken cancellationToken = default);

    Task SelectPublicFolderAsync(CancellationToken cancellationToken = default);
}
