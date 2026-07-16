using System.Text;
using BLE_APP.Models;
using Microsoft.Extensions.Logging;

namespace BLE_APP.Services;

public sealed class SensorLogService : ISensorLogService, IAsyncDisposable
{
    private readonly ILogger<SensorLogService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private StreamWriter? _writer;
    private long _connectionGeneration;
    private long _sampleCount;
    private DateTimeOffset _sessionStart;
    private string? _currentPath;

    public SensorLogService(ILogger<SensorLogService> logger)
    {
        _logger = logger;
        StatusText = "Logging disabled: select a valid log folder.";
    }

    public event EventHandler<string>? StatusChanged;

    public bool IsLogging { get; private set; }

    public string? CurrentLogName { get; private set; }

    public string StatusText { get; private set; }

    public string LogFolderDisplayText
        => SensorLogStorage.SelectedFolderDisplayName ?? "Not selected";

    public async Task StartSessionAsync(SensorReading firstReading, long connectionGeneration, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsLogging && _connectionGeneration == connectionGeneration)
            {
                return;
            }

            if (IsLogging)
            {
                await StopCoreAsync(LogStopReason.Cleanup, cancellationToken).ConfigureAwait(false);
            }

            _connectionGeneration = connectionGeneration;
            _sessionStart = firstReading.Timestamp;
            _sampleCount = 0;

            var target = await SensorLogStorage.OpenNewLogAsync(cancellationToken).ConfigureAwait(false);
            CurrentLogName = target.FileName;
            _currentPath = target.Location;
            _writer = new StreamWriter(target.Stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: false);
            await _writer.WriteLineAsync(SensorCsvFormatter.Header.AsMemory(), cancellationToken).ConfigureAwait(false);
            IsLogging = true;
            SetStatus($"Logging: {target.FileName}");
            await AppendCoreAsync(firstReading, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await StopCoreAsync(LogStopReason.Error, CancellationToken.None).ConfigureAwait(false);
            SetStatus(GetLoggingDisabledStatus(ex));
            _logger.LogWarning(ex, "Sensor log start failed.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AppendAsync(SensorReading reading, long connectionGeneration, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var startAttempt = false;
        try
        {
            if (!IsLogging)
            {
                startAttempt = true;
                await StartSessionAlreadyLockedAsync(reading, connectionGeneration, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (_connectionGeneration != connectionGeneration)
            {
                return;
            }

            await AppendCoreAsync(reading, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (startAttempt)
            {
                SetStatus(GetLoggingDisabledStatus(ex));
                _logger.LogWarning(ex, "Sensor log start skipped because no valid log folder is configured.");
            }
            else
            {
                await StopCoreAsync(LogStopReason.Error, CancellationToken.None).ConfigureAwait(false);
                SetStatus("Logging failed");
                _logger.LogWarning(ex, "Sensor log append failed.");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<LogSaveResult> StopAndSaveAsync(LogStopReason reason, long connectionGeneration, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!IsLogging || _connectionGeneration != connectionGeneration)
            {
                return new LogSaveResult(CurrentLogName, _currentPath, Saved: false, ErrorMessage: null);
            }

            return await StopCoreAsync(reason, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SelectPublicFolderAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var selected = await SensorLogStorage.SelectPublicFolderAsync(cancellationToken).ConfigureAwait(false);
            if (selected)
            {
                SetStatus("Logging folder ready");
            }
            else if (!SensorLogStorage.HasSelectedFolder)
            {
                SetStatus("Logging disabled: select a valid log folder.");
            }
        }
        catch (PlatformNotSupportedException)
        {
            SetStatus(SensorLogStorage.PublicFolderSelectionUnavailableMessage);
        }
        catch (Exception ex)
        {
            SetStatus($"Log folder setup failed: {ex.Message}");
            _logger.LogWarning(ex, "Selecting log folder failed.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopCoreAsync(LogStopReason.Shutdown, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private async Task StartSessionAlreadyLockedAsync(SensorReading firstReading, long connectionGeneration, CancellationToken cancellationToken)
    {
        _connectionGeneration = connectionGeneration;
        _sessionStart = firstReading.Timestamp;
        _sampleCount = 0;

        var target = await SensorLogStorage.OpenNewLogAsync(cancellationToken).ConfigureAwait(false);
        CurrentLogName = target.FileName;
        _currentPath = target.Location;
        _writer = new StreamWriter(target.Stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: false);
        await _writer.WriteLineAsync(SensorCsvFormatter.Header.AsMemory(), cancellationToken).ConfigureAwait(false);
        IsLogging = true;
        SetStatus($"Logging: {target.FileName}");
        await AppendCoreAsync(firstReading, cancellationToken).ConfigureAwait(false);
    }

    private async Task AppendCoreAsync(SensorReading reading, CancellationToken cancellationToken)
    {
        if (_writer is null)
        {
            return;
        }

        _sampleCount++;
        var row = SensorCsvFormatter.FormatRow(_sampleCount, _sessionStart, reading);
        await _writer.WriteLineAsync(row.AsMemory(), cancellationToken).ConfigureAwait(false);
        await _writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<LogSaveResult> StopCoreAsync(LogStopReason reason, CancellationToken cancellationToken)
    {
        var fileName = CurrentLogName;
        var path = _currentPath;

        if (_writer is not null)
        {
            SetStatus("Saving log");
            await _writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            await _writer.DisposeAsync().ConfigureAwait(false);
            _writer = null;
        }

        if (IsLogging)
        {
            IsLogging = false;
            SetStatus(fileName is null ? "Waiting for valid data" : $"Saved: {fileName}");
        }

        _logger.LogInformation("Sensor log stopped. Reason={Reason}; File={File}", reason, fileName);
        return new LogSaveResult(fileName, path, fileName is not null, null);
    }

    private void SetStatus(string status)
    {
        if (StatusText == status)
        {
            return;
        }

        StatusText = status;
        StatusChanged?.Invoke(this, status);
    }

    private static string GetLoggingDisabledStatus(Exception exception)
        => exception.Message.StartsWith("Logging disabled:", StringComparison.OrdinalIgnoreCase)
            ? exception.Message
            : "Logging disabled: select a valid log folder.";
}
