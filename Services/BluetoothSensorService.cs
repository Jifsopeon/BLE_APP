using Bluetooth.Abstractions;
using Bluetooth.Abstractions.Scanning;
using Bluetooth.Abstractions.Scanning.EventArgs;
using Bluetooth.Abstractions.Scanning.Exceptions;
using Bluetooth.Abstractions.Scanning.Options;
using Bluetooth.Maui;
using BLE_APP.Models;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text;
using System.Runtime.CompilerServices;
using System.Threading;

#if ANDROID
using Android.Bluetooth;
using Android.Content;
#endif
#if WINDOWS
using Windows.ApplicationModel;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;
#endif

namespace BLE_APP.Services;

public sealed class BluetoothSensorService : IBluetoothSensorService
{
    public static readonly Guid IaqServiceUuid = Guid.Parse("21c04d09-c884-4af1-96a9-52e4e4ba195b");
    public static readonly Guid SensorDataCharacteristicUuid = Guid.Parse("1e500043-6b31-4a3d-b91e-025f92ca9784");
    public static readonly Guid CommandCharacteristicUuid = Guid.Parse("1e500043-6b31-4a3d-b91e-025f92ca9785");
    public const string ExpectedDeviceName = "PSE84-IAQ";
    private static readonly Guid ClientCharacteristicConfigurationDescriptorUuid = Guid.Parse("00002902-0000-1000-8000-00805f9b34fb");

    private static readonly TimeSpan ScanStopTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan DiscoveryTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan SubscribeTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DescriptorDiscoveryTimeout = TimeSpan.FromSeconds(10);
#if DEBUG
    private const ulong RawPacketLogLimit = 5;
#endif
    private const ulong IncompatiblePacketWarningLimit = 5;
    private const ulong IncompatiblePacketWarningInterval = 100;
    private const int AndroidRequestedMtu = 247;
    private const int RequiredNotificationPayloadLength = SensorPacketDecoder.PacketLength;

    private readonly IBluetoothScanner _scanner;
    private readonly SensorPacketDecoder _decoder;
    private readonly ISensorLogService _sensorLog;
    private readonly ILogger<BluetoothSensorService> _logger;
    private readonly SemaphoreSlim _connectionGate = new(1, 1);
    private readonly object _scanGate = new();
    private readonly Dictionary<string, IBluetoothRemoteDevice> _devices = [];
#if !WINDOWS
    private readonly SemaphoreSlim _gattOperationGate = new(1, 1);
#endif

    private IBluetoothRemoteDevice? _currentDevice;
    private long _scanGeneration;
    private long _connectionGeneration;
    private bool _appScanActive;
#if !WINDOWS
    private IBluetoothRemoteService? _currentService;
    private IBluetoothRemoteCharacteristic? _sensorCharacteristic;
    private IBluetoothRemoteCharacteristic? _commandCharacteristic;
    private long _subscribedConnectionGeneration;
#endif
    private ushort? _lastSequenceNumber;
#if DEBUG
    private ulong _rawPacketLogCount;
#endif
    private ulong _validReadingLogCount;
    private ulong _incompatiblePacketWarningCount;
    private bool _userDisconnectRequested;
    private bool _disposed;

#if WINDOWS
    private BluetoothLEDevice? _windowsNativeDevice;
    private GattSession? _windowsGattSession;
    private GattDeviceService? _windowsIaqService;
    private GattCharacteristic? _windowsSensorCharacteristic;
    private GattCharacteristic? _windowsCommandCharacteristic;
    private bool _windowsSensorHandlerAttached;
#endif

    public BluetoothSensorService(
        IBluetoothScanner scanner,
        SensorPacketDecoder decoder,
        ISensorLogService sensorLog,
        ILogger<BluetoothSensorService> logger)
    {
        _scanner = scanner;
        _decoder = decoder;
        _sensorLog = sensorLog;
        _logger = logger;
        _scanner.DeviceListChanged += OnDeviceListChanged;
    }

    public event EventHandler<DiscoveredSensorDevice>? DeviceDiscovered;

    public event EventHandler<SensorReading>? ReadingReceived;

    public event EventHandler<string>? DiagnosticMessage;

    public event EventHandler? UnexpectedlyDisconnected;

    public BluetoothConnectionState State { get; private set; } = BluetoothConnectionState.Idle;

    public SensorReading? LatestReading { get; private set; }

    public ulong TotalPacketsReceived { get; private set; }

    public ulong MalformedPacketsReceived { get; private set; }

    public ulong SequenceGapsDetected { get; private set; }

    public DateTimeOffset? LastPacketTime { get; private set; }

    public bool CanSetManualLabel { get; private set; }

    public async Task<IReadOnlyList<DiscoveredSensorDevice>> ScanAsync(string? filter, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var connectionGeneration = Volatile.Read(ref _connectionGeneration);
        if (!TryBeginScan(out var scanGeneration))
        {
            LogLifecycle("Scan ignored because app-owned scan is already active", "ScanAsync", null, connectionGeneration, scanGeneration);
            return SnapshotDevices(filter);
        }

        LogLifecycle("Scan start", "ScanAsync", null, connectionGeneration, scanGeneration);

        try
        {
#if ANDROID
            if (!await AndroidBluetoothPermissionGate.EnsureBluetoothPermissionsAsync(message => Log(message), cancellationToken).ConfigureAwait(false))
            {
                SetState(BluetoothConnectionState.PermissionRequired);
                Log("Bluetooth permission denied.");
                return SnapshotDevices(filter);
            }
#endif

            if (!await _scanner.HasScannerPermissionsAsync().ConfigureAwait(false))
            {
                SetState(BluetoothConnectionState.PermissionRequired);
#if ANDROID
                Log("[ANDROID-PERMISSION] Bluetooth.Maui scanner permission status=missing after app permission gate");
#endif
                Log("Scanner permission is required.");
#if ANDROID
                return SnapshotDevices(filter);
#else
                await _scanner.RequestScannerPermissionsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
#endif
            }
#if ANDROID
            Log($"[ANDROID] Bluetooth permission status={(await _scanner.HasScannerPermissionsAsync().ConfigureAwait(false) ? "granted" : "denied")}");
            Log($"[ANDROID] Bluetooth adapter enabled={IsAndroidBluetoothAdapterEnabled()}");
#endif

            if (_scanner.IsRunning)
            {
                LogLifecycle("Platform scanner was already running before app scan start", "BeforeNewScan", null, connectionGeneration, scanGeneration);
                await StopPlatformScannerAsync("BeforeNewScan", null, connectionGeneration, scanGeneration, cancellationToken, ignoreAndroidTimeout: true).ConfigureAwait(false);
            }

            SetState(BluetoothConnectionState.Scanning);
            Log("Scan started.");

            await _scanner.ClearDevicesAsync().ConfigureAwait(false);
            _devices.Clear();

            var options = new ScanningOptions
            {
                IgnoreNamelessAdvertisements = false
            };

#if ANDROID
            var permissionOptions = new PermissionOptions
            {
                PermissionStrategy = PermissionRequestStrategy.ThrowIfNotGranted
            };
#else
            PermissionOptions? permissionOptions = null;
#endif
            await _scanner.StartScanningAsync(options, permissionOptions, timeout: TimeSpan.FromSeconds(10), cancellationToken: cancellationToken)
                .ConfigureAwait(false);
#if ANDROID
            Log($"[ANDROID-BLE] Scan started; ScanGeneration={scanGeneration}");
#endif
            await Task.Delay(timeout, cancellationToken).ConfigureAwait(false);

            var devices = SnapshotDevices(filter);
            SetState(devices.Count > 0 ? BluetoothConnectionState.DeviceFound : BluetoothConnectionState.Idle);
            if (devices.Count == 0)
            {
                Log("Expected device not found.");
            }

            return devices;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Log("Scan cancelled.");
            throw;
        }
        finally
        {
            EndScan(scanGeneration, "ScanFinally");
            await StopPlatformScannerAsync("ScanFinally", null, connectionGeneration, scanGeneration, CancellationToken.None, ignoreAndroidTimeout: true).ConfigureAwait(false);
            if (State == BluetoothConnectionState.Scanning)
            {
                SetState(BluetoothConnectionState.Idle);
            }
        }
    }

    public Task StopScanAsync(CancellationToken cancellationToken)
        => StopScanAsync(cancellationToken, reason: "ExternalStopScan");

    private async Task StopScanAsync(CancellationToken cancellationToken, string reason, [CallerMemberName] string caller = "")
    {
        var connectionGeneration = Volatile.Read(ref _connectionGeneration);
        var scanGeneration = CancelActiveScan(reason);
        LogLifecycle("Stop scan requested", reason, _currentDevice?.Id, connectionGeneration, scanGeneration, caller);
        await StopPlatformScannerAsync(reason, _currentDevice?.Id, connectionGeneration, scanGeneration, cancellationToken, ignoreAndroidTimeout: true).ConfigureAwait(false);

        if (State == BluetoothConnectionState.Scanning)
        {
            SetState(BluetoothConnectionState.Idle);
        }
    }

    public async Task ConnectAsync(string deviceId, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _connectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        long generation = 0;
        try
        {
            var currentGeneration = Volatile.Read(ref _connectionGeneration);
            if (IsActiveConnectionForDevice(deviceId))
            {
                LogLifecycle("Duplicate connect request ignored", "AlreadyConnected", deviceId, currentGeneration);
                return;
            }

            var previousGeneration = Volatile.Read(ref _connectionGeneration);
            await _sensorLog.StopAndSaveAsync(LogStopReason.Cleanup, previousGeneration, CancellationToken.None).ConfigureAwait(false);

            generation = Interlocked.Increment(ref _connectionGeneration);
            LogLifecycle("Connection start", "ConnectAsync", deviceId, generation);
            _userDisconnectRequested = false;

            if (!_devices.TryGetValue(deviceId, out var device))
            {
                device = _scanner.GetDevice(deviceId);
            }

            _currentDevice = device;

            var scanGeneration = CancelActiveScan("BeforeNewConnection");
            await StopPlatformScannerAsync("BeforeNewConnection", deviceId, generation, scanGeneration, CancellationToken.None, ignoreAndroidTimeout: true).ConfigureAwait(false);

            if (generation != Volatile.Read(ref _connectionGeneration))
            {
                LogLifecycle("Stale connection ignored before cleanup", "GenerationChanged", deviceId, generation);
                return;
            }

            await RunConnectionStepAsync("Cleanup previous connection", deviceId, async () => await CleanupConnectionAsync(skipDisconnect: false, reason: "BeforeNewConnection", generation, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);

#if WINDOWS
            SetState(BluetoothConnectionState.Connecting);
            Log($"Connection attempt started: {device.Name} ({device.Id}).");
            await ConnectWindowsNativeAsync(device, cancellationToken).ConfigureAwait(false);
            LogLifecycle("Connection ready", "ConnectWindowsNativeAsync", device.Id, generation);
            return;
#else
            AttachDeviceHandlers(device);

            SetState(BluetoothConnectionState.Connecting);
#if ANDROID
            Log("[ANDROID-BLE] Connecting");
#endif
            Log($"Connection attempt started: {device.Name} ({device.Id}).");

            await RunConnectionStepAsync("Clear stale services", device, async () => await device.ClearServicesAsync().ConfigureAwait(false)).ConfigureAwait(false);
            await RunConnectionStepAsync("Device connection: IBluetoothRemoteDevice.ConnectAsync", device, async () =>
            {
                await ConnectAndroidTransportWithOneGatt133RetryAsync(device, generation, cancellationToken).ConfigureAwait(false);
            }).ConfigureAwait(false);
            Log("BLE connected.");
#if ANDROID
            Log("[ANDROID-BLE] Connection state=Connected");
            await NegotiateAndroidMtuAsync(device, cancellationToken).ConfigureAwait(false);
#endif
            LogWindowsDeviceSnapshot("After ConnectAsync", device);

            SetState(BluetoothConnectionState.DiscoveringServices);
            LogGattPhase("Service discovery start", device.Id, generation, $"ServiceUuid={IaqServiceUuid}");
            await RunGattOperationAsync("Service discovery: ExploreServicesAsync uncached", device, generation, async () =>
            {
                await device.ExploreServicesAsync(new ServiceExplorationOptions
                {
                    Depth = ExplorationDepth.Characteristics,
                    ServiceUuidFilter = id => id == IaqServiceUuid,
                    UseCache = false
                }, DiscoveryTimeout, cancellationToken).ConfigureAwait(false);
            }).ConfigureAwait(false);
            LogGattPhase("Service discovery complete", device.Id, generation, $"ServiceUuid={IaqServiceUuid}");

            _currentService = RunConnectionStep("Service lookup", device, () => device.GetServiceOrDefault(IaqServiceUuid))
                ?? throw new InvalidOperationException($"IAQ service {IaqServiceUuid} was not found.");
            LogGattPhase("Service found", device.Id, generation, $"ServiceUuid={_currentService.Id}");
#if ANDROID
            Log("[ANDROID-BLE] Services discovered");
#endif

            _sensorCharacteristic = RunConnectionStep("Notification characteristic lookup", device, () => _currentService.GetCharacteristicOrDefault(SensorDataCharacteristicUuid))
                ?? throw new InvalidOperationException($"Sensor characteristic {SensorDataCharacteristicUuid} was not found.");
            _commandCharacteristic = RunConnectionStep("Command characteristic lookup", device, () => _currentService.GetCharacteristicOrDefault(CommandCharacteristicUuid))
                ?? throw new InvalidOperationException($"Command characteristic {CommandCharacteristicUuid} was not found.");
            CanSetManualLabel = true;
            Log($"[BLE-NOTIFY] Characteristic selected; DeviceId={device.Id}; ConnectionGeneration={generation}; ServiceUuid={_currentService.Id}; CharacteristicUuid={_sensorCharacteristic.Id}");
            LogGattPhase("Characteristic found", device.Id, generation, DescribeCharacteristic(_sensorCharacteristic, "SensorData"));
            LogGattPhase("Characteristic found", device.Id, generation, DescribeCharacteristic(_commandCharacteristic, "Command"));
#if ANDROID
            Log("[ANDROID-BLE] Notify characteristic found");
            Log("[ANDROID-BLE] Write characteristic found");
#endif

            if (!_sensorCharacteristic.CanListen)
            {
                throw new InvalidOperationException($"Connected, but sensor notification setup failed: characteristic {SensorDataCharacteristicUuid} does not support notifications or indications.");
            }
            Log($"[BLE-NOTIFY] Properties validated; DeviceId={device.Id}; ConnectionGeneration={generation}; CharacteristicUuid={_sensorCharacteristic.Id}; CanListen={_sensorCharacteristic.CanListen}");

            if (!_commandCharacteristic.CanWrite)
            {
                throw new InvalidOperationException($"Connected, but command setup failed: characteristic {CommandCharacteristicUuid} does not support writes.");
            }

            await RunGattOperationAsync("Sensor descriptor discovery: ExploreDescriptorsAsync uncached", device, generation, async () =>
            {
                await _sensorCharacteristic.ExploreDescriptorsAsync(new DescriptorExplorationOptions
                {
                    UseCache = false
                }, DescriptorDiscoveryTimeout, cancellationToken).ConfigureAwait(false);
            }).ConfigureAwait(false);
            LogDescriptorEnumeration(_sensorCharacteristic, device.Id, generation);

            var cccd = _sensorCharacteristic.GetDescriptorOrDefault(ClientCharacteristicConfigurationDescriptorUuid);
            if (cccd is null)
            {
                throw new InvalidOperationException($"Connected, but sensor notification setup failed: CCCD {ClientCharacteristicConfigurationDescriptorUuid} was not exposed for characteristic {SensorDataCharacteristicUuid}. Descriptors={FormatDescriptorIds(_sensorCharacteristic)}.");
            }
            LogGattPhase("CCCD discovered", device.Id, generation, $"DescriptorUuid={cccd.Id}; CanRead={cccd.CanRead}; CanWrite={cccd.CanWrite}");
            Log($"[BLE-NOTIFY] CCCD read capability; DeviceId={device.Id}; ConnectionGeneration={generation}; CanRead={cccd.CanRead}; CanWrite={cccd.CanWrite}; ReadRequired=False");

            _sensorCharacteristic.ValueUpdated -= OnSensorValueUpdated;
            _sensorCharacteristic.ValueUpdated += OnSensorValueUpdated;
            _subscribedConnectionGeneration = generation;

            SetState(BluetoothConnectionState.Subscribing);
            if (!_sensorCharacteristic.IsListening)
            {
                Log($"[BLE-NOTIFY] Native notification enable start; DeviceId={device.Id}; ConnectionGeneration={generation}; CharacteristicUuid={_sensorCharacteristic.Id}; SelectedMode=NotifyOrIndicate");
                LogGattPhase("Notification registration start", device.Id, generation, $"Mode=NotifyOrIndicate; CharacteristicUuid={_sensorCharacteristic.Id}; CCCD={cccd.Id}; CharacteristicLevelApi=StartListeningAsync; ExplicitCccdWrite=False");
                try
                {
                    await RunGattOperationAsync("Notification subscription: StartListeningAsync", device, generation, async () =>
                    {
                        await _sensorCharacteristic.StartListeningAsync(SubscribeTimeout, cancellationToken).ConfigureAwait(false);
                    }).ConfigureAwait(false);
                }
                catch (Exception ex) when (IsDescriptorCapabilityRejection(ex))
                {
                    throw new InvalidOperationException("Connected, but Android could not enable sensor notifications: the BLE wrapper rejected the CCCD write before the native GATT request.", ex);
                }
                catch (Exception ex) when (TryDescribeAndroidGattStatus(ex, out var status))
                {
                    throw new InvalidOperationException($"Connected, but sensor notification setup failed: Android descriptor write returned GATT status {status}.", ex);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Connected, but sensor notification setup failed: {ex.Message}", ex);
                }
                Log($"[BLE-NOTIFY] CCCD write callback; DeviceId={device.Id}; ConnectionGeneration={generation}; CharacteristicUuid={_sensorCharacteristic.Id}; DescriptorUuid={cccd.Id}; Success=True");
            }

            LogGattPhase("Notification registration complete", device.Id, generation, $"CharacteristicUuid={_sensorCharacteristic.Id}");
            Log($"[BLE-NOTIFY] Subscription active; DeviceId={device.Id}; ConnectionGeneration={generation}; CharacteristicUuid={_sensorCharacteristic.Id}");
#if ANDROID
            Log("[ANDROID-BLE] CCCD enabled");
#endif

            await RunGattOperationAsync("Command write: WriteValueAsync", device, generation, async () =>
            {
                await _commandCharacteristic.WriteValueAsync(new byte[] { 0x01 }, timeout: TimeSpan.FromSeconds(5), cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }).ConfigureAwait(false);
            Log("Start streaming command sent.");
#if ANDROID
            Log("[ANDROID-BLE] Start command written");
#endif

            SetState(BluetoothConnectionState.Connected);
#endif
        }
        catch (Exception ex)
        {
            SetState(BluetoothConnectionState.Error);
            LogConnectionFailureWithPhase(ex, generation);
            try
            {
                await CleanupConnectionAsync(skipDisconnect: false, reason: "ConnectFailure", generation, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception cleanupException)
            {
                Log($"Connection cleanup failed after preserving original error: {cleanupException.Message}", cleanupException);
            }

            SetState(BluetoothConnectionState.Disconnected);
            throw;
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    public async Task DisconnectAsync(bool userInitiated, CancellationToken cancellationToken)
    {
        await _connectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _userDisconnectRequested = userInitiated;
            var previousGeneration = Volatile.Read(ref _connectionGeneration);
            var generation = Interlocked.Increment(ref _connectionGeneration);
            LogLifecycle("Disconnect requested", userInitiated ? "UserDisconnect" : "ProgrammaticDisconnect", _currentDevice?.Id, generation);
            SetState(BluetoothConnectionState.Disconnecting);
            await _sensorLog.StopAndSaveAsync(userInitiated ? LogStopReason.UserDisconnect : LogStopReason.ProgrammaticDisconnect, previousGeneration, CancellationToken.None).ConfigureAwait(false);
            await CleanupConnectionAsync(skipDisconnect: false, reason: userInitiated ? "UserDisconnect" : "ProgrammaticDisconnect", generation, cancellationToken).ConfigureAwait(false);
            SetState(BluetoothConnectionState.Disconnected);
            Log("Cleanup completed.");
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    public async Task ReconnectAsync(CancellationToken cancellationToken)
    {
        var device = _currentDevice;
        if (device is null)
        {
            throw new InvalidOperationException("No previous device is available for reconnect.");
        }

        var deviceId = device.Id;
        var reconnectGeneration = Volatile.Read(ref _connectionGeneration);
        LogLifecycle("Reconnect start", "ReconnectAsync", deviceId, reconnectGeneration);
        for (var attempt = 1; attempt <= 3 && !_userDisconnectRequested; attempt++)
        {
            SetState(BluetoothConnectionState.Reconnecting);
            var delay = TimeSpan.FromSeconds(attempt * 2);
            Log($"Reconnection attempt {attempt} in {delay.TotalSeconds:0}s.");
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);

            try
            {
                await ConnectAsync(deviceId, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (attempt < 3)
            {
                Log($"Reconnect attempt {attempt} failed: {ex.Message}", ex);
            }
        }

        SetState(BluetoothConnectionState.Disconnected);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _scanner.DeviceListChanged -= OnDeviceListChanged;
        await DisconnectAsync(userInitiated: true, CancellationToken.None).ConfigureAwait(false);
        await _sensorLog.StopAndSaveAsync(LogStopReason.Shutdown, Volatile.Read(ref _connectionGeneration), CancellationToken.None).ConfigureAwait(false);
        await _scanner.DisposeAsync().ConfigureAwait(false);
        _connectionGate.Dispose();
#if !WINDOWS
        _gattOperationGate.Dispose();
#endif
    }

    private void OnDeviceListChanged(object? sender, DeviceListChangedEventArgs e)
    {
        if (!IsAppScanActive)
        {
            Log("[BLE-SCAN] Ignored scanner callback because no app-owned scan is active.");
            return;
        }

        foreach (var device in _scanner.GetDevices())
        {
            _devices[device.Id] = device;
            var sensorDevice = ToSensorDevice(device);
            DeviceDiscovered?.Invoke(this, sensorDevice);
            Log($"Device discovered: {sensorDevice.Name} RSSI {sensorDevice.SignalStrengthDbm} dBm.");
#if ANDROID
            Log($"[ANDROID-BLE] Device found name={sensorDevice.Name} id={sensorDevice.Id} rssi={sensorDevice.SignalStrengthDbm}");
#endif
        }
    }

    private void OnSensorValueUpdated(object? sender, ValueUpdatedEventArgs e)
    {
#if !WINDOWS
        if (_subscribedConnectionGeneration != Volatile.Read(ref _connectionGeneration))
        {
            Log($"[BLE-GATT] Ignored stale notification callback; SubscribedGeneration={_subscribedConnectionGeneration}; CurrentGeneration={Volatile.Read(ref _connectionGeneration)}.");
            return;
        }
#endif
        var bytes = e.NewValue.ToArray();
        ProcessSensorPacket(bytes);
    }

    private void ProcessSensorPacket(byte[] bytes)
    {
        var generation = Volatile.Read(ref _connectionGeneration);
        _ = Task.Run(() =>
        {
            var packetNumber = TotalPacketsReceived + MalformedPacketsReceived + 1UL;
#if ANDROID
            Log($"[ANDROID-BLE] Notification length={bytes.Length}");
#endif
#if DEBUG
            if (_rawPacketLogCount < RawPacketLogLimit)
            {
                _rawPacketLogCount++;
                Log($"RX {bytes.Length} bytes packet={packetNumber} timestamp={DateTimeOffset.Now:O}: {ToHexPayload(bytes)}");
            }
#endif

            try
            {
                var reading = _decoder.Decode(bytes);
                TrackSequence(reading.SequenceNumber);
                LatestReading = reading;
                TotalPacketsReceived++;
                LastPacketTime = reading.Timestamp;
                _ = _sensorLog.AppendAsync(reading, generation, CancellationToken.None);
#if ANDROID
                Log("[ANDROID-BLE] Packet decoded");
#endif
                _validReadingLogCount++;
                if (_validReadingLogCount == 1 || _validReadingLogCount % 100 == 0)
                {
                    System.Diagnostics.Debug.WriteLine("[BLE] Valid SensorReading received");
                    System.Diagnostics.Debug.WriteLine($"[BLE] PM1={reading.Pm1?.ToString() ?? "<null>"}");
                    System.Diagnostics.Debug.WriteLine($"[BLE] PM2.5={reading.Pm25?.ToString() ?? "<null>"}");
                    System.Diagnostics.Debug.WriteLine($"[BLE] PM4={reading.Pm4?.ToString() ?? "<null>"}");
                    System.Diagnostics.Debug.WriteLine($"[BLE] PM10={reading.Pm10?.ToString() ?? "<null>"}");
                    System.Diagnostics.Debug.WriteLine($"[BLE] Humidity={reading.Humidity?.ToString() ?? "<null>"}");
                    System.Diagnostics.Debug.WriteLine($"[BLE] Temperature={reading.Temperature?.ToString() ?? "<null>"}");
                    System.Diagnostics.Debug.WriteLine($"[BLE] VOC={reading.Voc?.ToString() ?? "<null>"}");
                    System.Diagnostics.Debug.WriteLine($"[BLE] NOx={reading.Nox?.ToString() ?? "<null>"}");
                    System.Diagnostics.Debug.WriteLine($"[BLE] CO2={reading.Co2?.ToString() ?? "<null>"}");
                    System.Diagnostics.Debug.WriteLine($"[BLE] Timestamp={reading.Timestamp:O}");
                }

                SetState(BluetoothConnectionState.ReceivingData);
                ReadingReceived?.Invoke(this, reading);
            }
            catch (Exception ex)
            {
                MalformedPacketsReceived++;
                if (ex is SensorPacketFormatException &&
                    ex.Message.StartsWith("Incompatible firmware packet:", StringComparison.Ordinal))
                {
                    _incompatiblePacketWarningCount++;
                    if (_incompatiblePacketWarningCount <= IncompatiblePacketWarningLimit ||
                        _incompatiblePacketWarningCount % IncompatiblePacketWarningInterval == 0)
                    {
                        Log($"{ex.Message} ExpectedLength={SensorPacketProtocol.PacketLength}. ActualLength={bytes.Length}. ConnectionGeneration={generation}. PacketCount={packetNumber}. Payload={ToHexPayload(bytes)}", ex);
                    }
                }
                else
                {
                    Log($"Malformed packet: {ex.Message}. Length={bytes.Length}. ConnectionGeneration={generation}. PacketCount={packetNumber}. Payload={ToHexPayload(bytes)}", ex);
                }
            }
        });
    }

    private static string ToHexPayload(byte[] bytes)
        => string.Join(" ", bytes.Select(value => value.ToString("X2")));

    public async Task SetManualLabelAsync(ManualLabelState label, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var payload = new[] { SensorPacketProtocol.SetManualLabelCommand, SensorPacketProtocol.EncodeManualLabel(label) };
        var generation = Volatile.Read(ref _connectionGeneration);

#if WINDOWS
        if (_windowsCommandCharacteristic is null)
        {
            throw new InvalidOperationException("Manual label command is unavailable until the command characteristic is discovered.");
        }

        using var writer = new DataWriter();
        writer.WriteBytes(payload);
        var status = await RunConnectionStepAsync(
            "Windows native API: Manual label WriteValueAsync",
            _currentDevice ?? throw new InvalidOperationException("No active BLE device."),
            async () => await _windowsCommandCharacteristic.WriteValueAsync(writer.DetachBuffer(), GattWriteOption.WriteWithResponse).AsTask(cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        Log($"Windows native Manual label command write result: {status}; Requested={SensorPacketProtocol.FormatManualLabel(label)}; Raw={payload[1]}.");
        if (status != GattCommunicationStatus.Success)
        {
            throw new InvalidOperationException($"Manual label command write returned {status}.");
        }
#else
        if (_currentDevice is null || _commandCharacteristic is null)
        {
            throw new InvalidOperationException("Manual label command is unavailable until the command characteristic is discovered.");
        }

        await RunGattOperationAsync("Manual label command: WriteValueAsync", _currentDevice, generation, async () =>
        {
            await _commandCharacteristic.WriteValueAsync(payload, timeout: TimeSpan.FromSeconds(5), cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }).ConfigureAwait(false);
        Log($"Manual label command written. Requested={SensorPacketProtocol.FormatManualLabel(label)}; Raw={payload[1]}.");
#endif
    }

#if ANDROID
    private async Task NegotiateAndroidMtuAsync(IBluetoothRemoteDevice device, CancellationToken cancellationToken)
    {
        try
        {
            Log($"[ANDROID-BLE] MTU request={AndroidRequestedMtu}");
            var negotiatedMtu = await device.RequestMtuAsync(AndroidRequestedMtu, TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            var notificationPayload = Math.Max(0, negotiatedMtu - 3);
            Log($"[ANDROID-BLE] MTU negotiated={negotiatedMtu}");
            Log($"[ANDROID-BLE] MTU payload supports {RequiredNotificationPayloadLength} bytes={notificationPayload >= RequiredNotificationPayloadLength}");
            if (notificationPayload < RequiredNotificationPayloadLength)
            {
                throw new InvalidOperationException($"Negotiated ATT MTU {negotiatedMtu} only supports {notificationPayload} notification bytes; {RequiredNotificationPayloadLength} are required.");
            }
        }
        catch (Exception ex)
        {
            Log($"[ANDROID-BLE] MTU negotiation failed: {ex.Message}", ex);
            throw;
        }
    }

    private static bool IsAndroidBluetoothAdapterEnabled()
    {
        var manager = Microsoft.Maui.ApplicationModel.Platform.AppContext.GetSystemService(Context.BluetoothService) as BluetoothManager;
        return manager?.Adapter?.IsEnabled == true;
    }
#endif

    private void OnUnexpectedDisconnection(object? sender, DeviceUnexpectedDisconnectionEventArgs e)
    {
        if (_userDisconnectRequested)
        {
            return;
        }

        _ = _sensorLog.StopAndSaveAsync(LogStopReason.UnexpectedDisconnect, Volatile.Read(ref _connectionGeneration), CancellationToken.None);
        SetState(BluetoothConnectionState.Disconnected);
        Log("Device disconnected unexpectedly.");
        UnexpectedlyDisconnected?.Invoke(this, EventArgs.Empty);
    }

    private IReadOnlyList<DiscoveredSensorDevice> SnapshotDevices(string? filter)
    {
        IEnumerable<IBluetoothRemoteDevice> devices = _devices.Values;
        if (!string.IsNullOrWhiteSpace(filter))
        {
            devices = devices.Where(device => MatchesFilter(device, filter));
        }

        return devices.Select(ToSensorDevice)
            .OrderByDescending(device => device.AdvertisesExpectedService)
            .ThenByDescending(device => device.SignalStrengthDbm)
            .ToList();
    }

    private static bool MatchesFilter(IBluetoothRemoteDevice device, string filter)
        => device.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
           device.Id.Contains(filter, StringComparison.OrdinalIgnoreCase);

    private static DiscoveredSensorDevice ToSensorDevice(IBluetoothRemoteDevice device)
    {
        var advertisesService = device.LastAdvertisement?.ServicesGuids.Contains(IaqServiceUuid) == true;
        return new DiscoveredSensorDevice(
            device.Id,
            string.IsNullOrWhiteSpace(device.Name) ? "(unnamed)" : device.Name,
            device.SignalStrengthDbm,
            advertisesService,
            device.LastSeen);
    }

    private void AttachDeviceHandlers(IBluetoothRemoteDevice device)
    {
        device.UnexpectedDisconnection -= OnUnexpectedDisconnection;
        device.UnexpectedDisconnection += OnUnexpectedDisconnection;
    }

    private async ValueTask CleanupConnectionAsync(bool skipDisconnect, string reason, long generation, CancellationToken cancellationToken, [CallerMemberName] string caller = "")
    {
        LogLifecycle("Cleanup requested", reason, _currentDevice?.Id, generation, caller);
        if (generation != Volatile.Read(ref _connectionGeneration))
        {
            LogLifecycle("Stale cleanup ignored", reason, _currentDevice?.Id, generation, caller);
            return;
        }

#if WINDOWS
        await CleanupWindowsNativeConnectionAsync(disableNotifications: !skipDisconnect, cancellationToken).ConfigureAwait(false);
#endif

#if !WINDOWS
        if (_sensorCharacteristic is not null)
        {
            _sensorCharacteristic.ValueUpdated -= OnSensorValueUpdated;
            if (_sensorCharacteristic.IsListening)
            {
                await _sensorCharacteristic.StopListeningAsync(SubscribeTimeout, cancellationToken).ConfigureAwait(false);
            }
        }

        if (_currentDevice is not null)
        {
            _currentDevice.UnexpectedDisconnection -= OnUnexpectedDisconnection;
            if (!skipDisconnect && _currentDevice.IsConnected)
            {
                _currentDevice.IgnoreNextUnexpectedDisconnection = true;
                await _currentDevice.DisconnectIfNeededAsync(ConnectTimeout, cancellationToken).ConfigureAwait(false);
            }
        }

        _sensorCharacteristic = null;
        _commandCharacteristic = null;
        _currentService = null;
        _subscribedConnectionGeneration = 0;
#else
        if (_currentDevice is not null)
        {
            _currentDevice.UnexpectedDisconnection -= OnUnexpectedDisconnection;
        }
#endif
        _lastSequenceNumber = null;
        CanSetManualLabel = false;
    }

    private void TrackSequence(ushort sequenceNumber)
    {
        if (_lastSequenceNumber is ushort previous)
        {
            var expected = unchecked((ushort)(previous + 1));
            if (sequenceNumber != expected)
            {
                SequenceGapsDetected++;
                Log($"Sequence gap detected. Expected {expected}, received {sequenceNumber}.");
            }
        }

        _lastSequenceNumber = sequenceNumber;
    }

    private void LogWindowsDeviceSnapshot(string operation, IBluetoothRemoteDevice device)
    {
#if WINDOWS
        Log($"{operation}: {GetWindowsPackageContext()} {GetWindowsDeviceContext(device)}");
#else
        _ = operation;
        _ = device;
#endif
    }

    private bool IsActiveConnectionForDevice(string deviceId)
    {
#if WINDOWS
        return _currentDevice?.Id == deviceId
               && _windowsNativeDevice is not null
               && _windowsSensorCharacteristic is not null
               && _windowsGattSession is not null
               && (State == BluetoothConnectionState.Connected || State == BluetoothConnectionState.ReceivingData);
#else
        return _currentDevice?.Id == deviceId
               && (State == BluetoothConnectionState.Connected || State == BluetoothConnectionState.ReceivingData);
#endif
    }

    private bool IsAppScanActive
    {
        get
        {
            lock (_scanGate)
            {
                return _appScanActive;
            }
        }
    }

    private bool TryBeginScan(out long scanGeneration)
    {
        lock (_scanGate)
        {
            if (_appScanActive)
            {
                scanGeneration = _scanGeneration;
                return false;
            }

            scanGeneration = ++_scanGeneration;
            _appScanActive = true;
            return true;
        }
    }

    private void EndScan(long scanGeneration, string reason)
    {
        lock (_scanGate)
        {
            if (scanGeneration != _scanGeneration)
            {
                Log($"[BLE-SCAN] Ignored stale scan completion; Reason={reason}; ScanGeneration={scanGeneration}; CurrentScanGeneration={_scanGeneration}; AppScanActive={_appScanActive}.");
                return;
            }

            _appScanActive = false;
            Log($"[BLE-SCAN] App-owned scan ended; Reason={reason}; ScanGeneration={scanGeneration}; PlatformScannerRunning={_scanner.IsRunning}.");
        }
    }

    private long CancelActiveScan(string reason)
    {
        lock (_scanGate)
        {
            if (_appScanActive)
            {
                _appScanActive = false;
                _scanGeneration++;
                Log($"[BLE-SCAN] App-owned scan cancelled; Reason={reason}; ScanGeneration={_scanGeneration}; PlatformScannerRunning={_scanner.IsRunning}.");
            }

            return _scanGeneration;
        }
    }

    private async Task StopPlatformScannerAsync(
        string reason,
        string? deviceId,
        long connectionGeneration,
        long scanGeneration,
        CancellationToken cancellationToken,
        bool ignoreAndroidTimeout,
        [CallerMemberName] string caller = "")
    {
        LogLifecycle("Platform scanner stop requested", reason, deviceId, connectionGeneration, scanGeneration, caller);
        if (!_scanner.IsRunning)
        {
            Log($"[BLE-SCAN] Platform scanner already stopped; Reason={reason}; ScanGeneration={scanGeneration}.");
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            await _scanner.StopScanningIfNeededAsync(ScanStopTimeout, cancellationToken).ConfigureAwait(false);
            Log($"[BLE-SCAN] Platform scanner stopped; Reason={reason}; ScanGeneration={scanGeneration}; ElapsedMs={stopwatch.ElapsedMilliseconds}; PlatformScannerRunning={_scanner.IsRunning}.");
        }
        catch (Exception ex) when (ShouldIgnoreScannerStopException(ex, ignoreAndroidTimeout))
        {
            Log($"[ANDROID-BLE] Scanner-stop timeout ignored as cleanup warning; Reason={reason}; ScanGeneration={scanGeneration}; ElapsedMs={stopwatch.ElapsedMilliseconds}; AppScanActive={IsAppScanActive}; PlatformScannerRunning={_scanner.IsRunning}; ConnectionGeneration={connectionGeneration}.", ex);
        }
        catch (Exception ex) when (!_scanner.IsRunning)
        {
            Log($"[BLE-SCAN] Platform scanner stop reported {ex.GetType().Name}, but scanner is already stopped; Reason={reason}; ScanGeneration={scanGeneration}; ElapsedMs={stopwatch.ElapsedMilliseconds}.", ex);
        }
    }

    private static bool ShouldIgnoreScannerStopException(Exception exception, bool ignoreAndroidTimeout)
    {
#if ANDROID
        return ignoreAndroidTimeout && exception is TimeoutException;
#else
        _ = exception;
        _ = ignoreAndroidTimeout;
        return false;
#endif
    }

    private void LogLifecycle(string action, string reason, string? deviceId, long generation, [CallerMemberName] string caller = "")
        => LogLifecycle(action, reason, deviceId, generation, Volatile.Read(ref _scanGeneration), caller);

    private void LogLifecycle(string action, string reason, string? deviceId, long connectionGeneration, long scanGeneration, [CallerMemberName] string caller = "")
        => Log($"[BLE-LIFECYCLE] {action}; Reason={reason}; State={State}; DeviceId={deviceId ?? "<none>"}; ConnectionGeneration={connectionGeneration}; ScanGeneration={scanGeneration}; AppScanActive={IsAppScanActive}; PlatformScannerRunning={_scanner.IsRunning}; ThreadId={Environment.CurrentManagedThreadId}; CallSite={caller}.");

    private T RunConnectionStep<T>(string operation, IBluetoothRemoteDevice device, Func<T> action)
    {
        Log($"{operation} started. DeviceId={device.Id}. {GetPlatformContext(device)}");
        try
        {
            var result = action();
            Log($"{operation} succeeded. DeviceId={device.Id}. {GetPlatformContext(device)}");
            return result;
        }
        catch (Exception ex)
        {
            LogConnectionException(operation, device.Id, device, ex);
            throw;
        }
    }

    private Task RunConnectionStepAsync(string operation, string deviceId, Func<Task> action)
        => RunConnectionStepAsync(operation, deviceId, null, action);

    private Task RunConnectionStepAsync(string operation, IBluetoothRemoteDevice device, Func<Task> action)
        => RunConnectionStepAsync(operation, device.Id, device, action);

#if !WINDOWS
    private async Task ConnectAndroidTransportWithOneGatt133RetryAsync(IBluetoothRemoteDevice device, long generation, CancellationToken cancellationToken)
    {
        const int maxAttempts = 2;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                Log($"[BLE-CONNECT] Invoking platform connection; DeviceId={device.Id}; ConnectionGeneration={generation}; Attempt={attempt}; MaxAttempts={maxAttempts}; AppScanActive={IsAppScanActive}; PlatformScannerRunning={_scanner.IsRunning}; ThreadId={Environment.CurrentManagedThreadId}.");
                await device.ConnectAsync(new ConnectionOptions { WaitForAdvertisementBeforeConnecting = false }, ConnectTimeout, cancellationToken)
                    .ConfigureAwait(false);
                Log($"[BLE-CONNECT] Platform connection returned; DeviceId={device.Id}; ConnectionGeneration={generation}; Attempt={attempt}.");
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts && IsAndroidGatt133(ex))
            {
                Log($"[BLE-CONNECT] Android GATT status 133 detected; closing failed session is handled by AndroidBluetoothRemoteDevice and one fresh retry will be attempted. DeviceId={device.Id}; ConnectionGeneration={generation}; Attempt={attempt}; Error={ex.Message}", ex);
                await device.ClearServicesAsync().ConfigureAwait(false);
            }
        }
    }

    private static bool IsAndroidGatt133(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            var typeName = current.GetType().FullName ?? current.GetType().Name;
            if (typeName.Contains("AndroidNativeGattCallbackStatus", StringComparison.Ordinal)
                && (current.Message.Contains("GATT_ERROR", StringComparison.OrdinalIgnoreCase)
                    || current.Message.Contains("Generic GATT error", StringComparison.OrdinalIgnoreCase)
                    || current.Message.Contains("133", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDescriptorCapabilityRejection(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is DescriptorCantWriteException)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryDescribeAndroidGattStatus(Exception exception, out string status)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            var typeName = current.GetType().FullName ?? current.GetType().Name;
            if (typeName.Contains("AndroidNativeGattCallbackStatus", StringComparison.Ordinal))
            {
                status = current.Message;
                return true;
            }
        }

        status = string.Empty;
        return false;
    }

    private async Task RunGattOperationAsync(string operation, IBluetoothRemoteDevice device, long generation, Func<Task> action)
    {
        if (generation != Volatile.Read(ref _connectionGeneration))
        {
            LogGattPhase("Stale GATT operation skipped", device.Id, generation, $"Operation={operation}");
            return;
        }

        LogGattPhase($"{operation} start", device.Id, generation);
        await _gattOperationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (generation != Volatile.Read(ref _connectionGeneration))
            {
                LogGattPhase("Stale GATT operation skipped after queue", device.Id, generation, $"Operation={operation}");
                return;
            }

            await action().ConfigureAwait(false);
            LogGattPhase($"{operation} complete", device.Id, generation);
        }
        catch (Exception ex)
        {
            LogConnectionException(operation, device.Id, device, ex);
            throw ClassifyGattInitializationException(operation, ex);
        }
        finally
        {
            _gattOperationGate.Release();
        }
    }

    private static Exception ClassifyGattInitializationException(string operation, Exception exception)
    {
        if (exception.Message.StartsWith("Connected, but", StringComparison.OrdinalIgnoreCase))
        {
            return exception;
        }

        if (operation.Contains("Service discovery", StringComparison.OrdinalIgnoreCase))
        {
            return new InvalidOperationException($"Connected, but service discovery failed: {exception.Message}", exception);
        }

        if (operation.Contains("descriptor", StringComparison.OrdinalIgnoreCase))
        {
            return new InvalidOperationException($"Connected, but sensor descriptor discovery failed: {exception.Message}", exception);
        }

        if (operation.Contains("Notification", StringComparison.OrdinalIgnoreCase))
        {
            return new InvalidOperationException($"Connected, but sensor notification setup failed: {exception.Message}", exception);
        }

        if (operation.Contains("Command write", StringComparison.OrdinalIgnoreCase))
        {
            return new InvalidOperationException($"Connected, but start-streaming command failed: {exception.Message}", exception);
        }

        return exception;
    }
#endif

    private async Task RunConnectionStepAsync(string operation, string deviceId, IBluetoothRemoteDevice? device, Func<Task> action)
    {
        Log($"{operation} started. DeviceId={deviceId}. {GetPlatformContext(device)}");
        try
        {
            await action().ConfigureAwait(false);
            Log($"{operation} succeeded. DeviceId={deviceId}. {GetPlatformContext(device)}");
        }
        catch (Exception ex)
        {
            LogConnectionException(operation, deviceId, device, ex);
            throw;
        }
    }

    private async Task<T> RunConnectionStepAsync<T>(string operation, IBluetoothRemoteDevice device, Func<Task<T>> action)
    {
        Log($"{operation} started. DeviceId={device.Id}. {GetPlatformContext(device)}");
        try
        {
            var result = await action().ConfigureAwait(false);
            Log($"{operation} succeeded. DeviceId={device.Id}. {GetPlatformContext(device)}");
            return result;
        }
        catch (Exception ex)
        {
            LogConnectionException(operation, device.Id, device, ex);
            throw;
        }
    }

    private void LogConnectionException(string operation, string deviceId, IBluetoothRemoteDevice? device, Exception exception)
    {
        var details = new StringBuilder()
            .AppendLine($"{operation} failed.")
            .AppendLine($"DeviceId: {deviceId}")
            .AppendLine(GetPlatformContext(device))
            .AppendLine("Exception detail:");

        AppendExceptionDetails(details, exception, 0);
        Log(details.ToString().TrimEnd(), exception);
    }

    private void LogConnectionFailureWithPhase(Exception exception, long generation)
    {
        var phase = exception.Message.StartsWith("Connected, but", StringComparison.OrdinalIgnoreCase)
            ? "GATT initialization failed after transport connection"
            : "Transport connection or pre-GATT initialization failed";

        Log($"{phase}; ConnectionGeneration={generation}; Error={exception.Message}", exception);
    }

#if !WINDOWS
    private void LogGattPhase(string phase, string deviceId, long generation, string? details = null)
    {
        var suffix = string.IsNullOrWhiteSpace(details) ? string.Empty : $"; {details}";
        Log($"[BLE-GATT] {phase}; DeviceId={deviceId}; ConnectionGeneration={generation}{suffix}");
    }

    private static string DescribeCharacteristic(IBluetoothRemoteCharacteristic characteristic, string role)
        => $"Role={role}; ServiceUuid={characteristic.Service.Id}; CharacteristicUuid={characteristic.Id}; CanRead={characteristic.CanRead}; CanWrite={characteristic.CanWrite}; CanListen={characteristic.CanListen}; IsListening={characteristic.IsListening}";

    private void LogDescriptorEnumeration(IBluetoothRemoteCharacteristic characteristic, string deviceId, long generation)
    {
        var descriptors = characteristic.GetDescriptors().ToList();
        LogGattPhase("Descriptor enumeration", deviceId, generation, $"ServiceUuid={characteristic.Service.Id}; CharacteristicUuid={characteristic.Id}; CanListen={characteristic.CanListen}; IsListening={characteristic.IsListening}; Count={descriptors.Count}; DescriptorUuids={FormatDescriptorIds(descriptors)}; DescriptorCapabilities={FormatDescriptorCapabilities(descriptors)}; CharacteristicLevelApi=StartListeningAsync; ExplicitCccdWrite=False");
    }

    private static string FormatDescriptorIds(IBluetoothRemoteCharacteristic characteristic)
        => FormatDescriptorIds(characteristic.GetDescriptors());

    private static string FormatDescriptorIds(IEnumerable<IBluetoothRemoteDescriptor> descriptors)
    {
        var descriptorIds = descriptors.Select(descriptor => descriptor.Id.ToString()).ToList();
        return descriptorIds.Count == 0 ? "<none>" : string.Join(",", descriptorIds);
    }

    private static string FormatDescriptorCapabilities(IEnumerable<IBluetoothRemoteDescriptor> descriptors)
    {
        var descriptorCapabilities = descriptors
            .Select(descriptor => $"{descriptor.Id}:CanRead={descriptor.CanRead},CanWrite={descriptor.CanWrite}")
            .ToList();
        return descriptorCapabilities.Count == 0 ? "<none>" : string.Join("|", descriptorCapabilities);
    }
#endif

    private static void AppendExceptionDetails(StringBuilder builder, Exception exception, int depth)
    {
        var prefix = depth == 0 ? string.Empty : $"Inner[{depth}] ";
        builder
            .AppendLine($"{prefix}Type: {exception.GetType().FullName}")
            .AppendLine($"{prefix}Message: {exception.Message}")
            .AppendLine($"{prefix}HResult: 0x{exception.HResult:X8}");

        if (exception is AggregateException aggregateException)
        {
            var flattened = aggregateException.Flatten();
            for (var i = 0; i < flattened.InnerExceptions.Count; i++)
            {
                builder.AppendLine($"{prefix}AggregateInner[{i}]:");
                AppendExceptionDetails(builder, flattened.InnerExceptions[i], depth + 1);
            }
        }

        if (exception.InnerException is not null && exception is not AggregateException)
        {
            AppendExceptionDetails(builder, exception.InnerException, depth + 1);
        }

        if (!string.IsNullOrWhiteSpace(exception.StackTrace))
        {
            builder.AppendLine($"{prefix}StackTrace:");
            builder.AppendLine(exception.StackTrace);
        }
    }

    private string GetPlatformContext(IBluetoothRemoteDevice? device)
    {
#if WINDOWS
        return $"{GetWindowsPackageContext()} {GetWindowsDeviceContext(device)}";
#else
        _ = device;
        return $"Platform: non-Windows; AppScanActive={IsAppScanActive}; PlatformScannerRunning={_scanner.IsRunning}; ScanGeneration={Volatile.Read(ref _scanGeneration)};";
#endif
    }

#if WINDOWS
    private async Task ConnectWindowsNativeAsync(IBluetoothRemoteDevice device, CancellationToken cancellationToken)
    {
        Log("Windows BLE connection owner: native WinRT GATT. Laerdal ConnectAsync is not used on Windows.");

        var address = RunConnectionStep("Windows native address parse", device, () => ParseBluetoothAddress(device.Id));

        _windowsNativeDevice = await RunConnectionStepAsync(
            "Windows native API: BluetoothLEDevice.FromBluetoothAddressAsync",
            device,
            async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var nativeDevice = await BluetoothLEDevice.FromBluetoothAddressAsync(address).AsTask(cancellationToken).ConfigureAwait(false);
                return nativeDevice ?? throw new InvalidOperationException($"BluetoothLEDevice.FromBluetoothAddressAsync returned null for {device.Id}.");
            }).ConfigureAwait(false);

        _windowsNativeDevice.ConnectionStatusChanged += OnWindowsNativeConnectionStatusChanged;
        Log($"Windows native device created. {DescribeNativeDevice(_windowsNativeDevice)}");

        var accessStatus = await RunConnectionStepAsync(
            "Windows native API: BluetoothLEDevice.RequestAccessAsync",
            device,
            async () => await _windowsNativeDevice.RequestAccessAsync().AsTask(cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        Log($"Windows native access status: {accessStatus}. {DescribeNativeDevice(_windowsNativeDevice)}");

        if (accessStatus != DeviceAccessStatus.Allowed)
        {
            throw new UnauthorizedAccessException($"BluetoothLEDevice.RequestAccessAsync returned {accessStatus} for {device.Id}.");
        }

        _windowsGattSession = await RunConnectionStepAsync(
            "Windows native API: GattSession.FromDeviceIdAsync",
            device,
            async () =>
            {
                var session = await GattSession.FromDeviceIdAsync(_windowsNativeDevice.BluetoothDeviceId).AsTask(cancellationToken).ConfigureAwait(false);
                return session ?? throw new InvalidOperationException($"GattSession.FromDeviceIdAsync returned null for {device.Id}.");
            }).ConfigureAwait(false);
        _windowsGattSession.SessionStatusChanged += OnWindowsGattSessionStatusChanged;
        if (_windowsGattSession.CanMaintainConnection)
        {
            _windowsGattSession.MaintainConnection = true;
        }

        Log($"Windows native GATT session opened. Status={_windowsGattSession.SessionStatus}; CanMaintainConnection={_windowsGattSession.CanMaintainConnection}; MaintainConnection={_windowsGattSession.MaintainConnection}; MaxPduSize={_windowsGattSession.MaxPduSize}.");

        SetState(BluetoothConnectionState.DiscoveringServices);
        var servicesResult = await RunConnectionStepAsync(
            "Windows native API: BluetoothLEDevice.GetGattServicesAsync(Uncached)",
            device,
            async () => await _windowsNativeDevice.GetGattServicesAsync(BluetoothCacheMode.Uncached).AsTask(cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        Log($"Windows native service discovery result: status={servicesResult.Status}, serviceCount={servicesResult.Services.Count}.");
        if (servicesResult.Status != GattCommunicationStatus.Success)
        {
            throw new InvalidOperationException($"BluetoothLEDevice.GetGattServicesAsync(Uncached) returned {servicesResult.Status}.");
        }

        foreach (var service in servicesResult.Services)
        {
            Log($"Windows native service discovered: {service.Uuid}.");
            if (service.Uuid == IaqServiceUuid)
            {
                _windowsIaqService = service;
            }
            else
            {
                service.Dispose();
            }
        }

        if (_windowsIaqService is null)
        {
            throw new InvalidOperationException($"IAQ service {IaqServiceUuid} was not found.");
        }

        Log("Service found.");

        var characteristicsResult = await RunConnectionStepAsync(
            "Windows native API: GattDeviceService.GetCharacteristicsAsync(Uncached)",
            device,
            async () => await _windowsIaqService.GetCharacteristicsAsync(BluetoothCacheMode.Uncached).AsTask(cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        Log($"Windows native characteristic discovery result: status={characteristicsResult.Status}, characteristicCount={characteristicsResult.Characteristics.Count}.");
        if (characteristicsResult.Status != GattCommunicationStatus.Success)
        {
            throw new InvalidOperationException($"GattDeviceService.GetCharacteristicsAsync(Uncached) returned {characteristicsResult.Status}.");
        }

        foreach (var characteristic in characteristicsResult.Characteristics)
        {
            Log($"Windows native characteristic discovered: {characteristic.Uuid}; Properties={characteristic.CharacteristicProperties}.");
            if (characteristic.Uuid == SensorDataCharacteristicUuid)
            {
                _windowsSensorCharacteristic = characteristic;
            }
            else if (characteristic.Uuid == CommandCharacteristicUuid)
            {
                _windowsCommandCharacteristic = characteristic;
            }
        }

        if (_windowsSensorCharacteristic is null)
        {
            throw new InvalidOperationException($"Sensor characteristic {SensorDataCharacteristicUuid} was not found.");
        }

        if (_windowsCommandCharacteristic is null)
        {
            throw new InvalidOperationException($"Command characteristic {CommandCharacteristicUuid} was not found.");
        }

        if ((_windowsSensorCharacteristic.CharacteristicProperties & GattCharacteristicProperties.Notify) == 0)
        {
            throw new InvalidOperationException("Sensor characteristic does not support notifications.");
        }

        if ((_windowsCommandCharacteristic.CharacteristicProperties & GattCharacteristicProperties.Write) == 0
            && (_windowsCommandCharacteristic.CharacteristicProperties & GattCharacteristicProperties.WriteWithoutResponse) == 0)
        {
            throw new InvalidOperationException("Command characteristic does not support writes.");
        }

        CanSetManualLabel = true;

        _windowsSensorCharacteristic.ValueChanged -= OnWindowsSensorValueChanged;
        _windowsSensorCharacteristic.ValueChanged += OnWindowsSensorValueChanged;
        _windowsSensorHandlerAttached = true;
        Log($"[BLE-RX] GattSession.MaxPduSize={_windowsGattSession.MaxPduSize} ExpectedNotificationValueLength={RequiredNotificationPayloadLength} ValueChangedHandlerAttached={_windowsSensorHandlerAttached} CCCDEnabled=false");

        SetState(BluetoothConnectionState.Subscribing);
        var cccdStatus = await RunConnectionStepAsync(
            "Windows native API: WriteClientCharacteristicConfigurationDescriptorAsync(Notify)",
            device,
            async () => await _windowsSensorCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify).AsTask(cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        Log($"Windows native CCCD write result: {cccdStatus}.");
        if (cccdStatus != GattCommunicationStatus.Success)
        {
            throw new InvalidOperationException($"Sensor notification CCCD write returned {cccdStatus}.");
        }

        Log("Notification subscription succeeded.");
        Log($"[BLE-RX] GattSession.MaxPduSize={_windowsGattSession.MaxPduSize} ExpectedNotificationValueLength={RequiredNotificationPayloadLength} ValueChangedHandlerAttached={_windowsSensorHandlerAttached} CCCDEnabled=true");

        using var writer = new DataWriter();
        writer.WriteBytes(new byte[] { 0x01 });
        var commandStatus = await RunConnectionStepAsync(
            "Windows native API: Command WriteValueAsync",
            device,
            async () => await _windowsCommandCharacteristic.WriteValueAsync(writer.DetachBuffer(), GattWriteOption.WriteWithResponse).AsTask(cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        Log($"Windows native command write result: {commandStatus}.");
        if (commandStatus != GattCommunicationStatus.Success)
        {
            throw new InvalidOperationException($"Start streaming command write returned {commandStatus}.");
        }

        Log("Start streaming command sent.");
        SetState(BluetoothConnectionState.Connected);
    }

    private async ValueTask CleanupWindowsNativeConnectionAsync(bool disableNotifications, CancellationToken cancellationToken)
    {
        if (_windowsSensorCharacteristic is not null)
        {
            if (disableNotifications)
            {
                try
                {
                    var cccdStatus = await _windowsSensorCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.None)
                        .AsTask(cancellationToken)
                        .ConfigureAwait(false);
                    Log($"Windows native CCCD disable result: {cccdStatus}.");
                }
                catch (Exception ex)
                {
                    Log($"Windows native CCCD disable failed during cleanup: {ex.Message}", ex);
                }
            }

            if (_windowsSensorHandlerAttached)
            {
                _windowsSensorCharacteristic.ValueChanged -= OnWindowsSensorValueChanged;
                _windowsSensorHandlerAttached = false;
            }
        }

        if (_windowsGattSession is not null)
        {
            _windowsGattSession.SessionStatusChanged -= OnWindowsGattSessionStatusChanged;
            if (_windowsGattSession.CanMaintainConnection)
            {
                _windowsGattSession.MaintainConnection = false;
            }

            _windowsGattSession.Dispose();
            _windowsGattSession = null;
        }

        if (_windowsNativeDevice is not null)
        {
            _windowsNativeDevice.ConnectionStatusChanged -= OnWindowsNativeConnectionStatusChanged;
            _windowsNativeDevice.Dispose();
            _windowsNativeDevice = null;
        }

        _windowsIaqService?.Dispose();
        _windowsIaqService = null;
        _windowsSensorCharacteristic = null;
        _windowsCommandCharacteristic = null;
        CanSetManualLabel = false;
    }

    private void OnWindowsSensorValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        try
        {
            Log("[BLE-RX] ValueChanged callback entered");
            var reader = DataReader.FromBuffer(args.CharacteristicValue);
            var bytes = new byte[(int)reader.UnconsumedBufferLength];
            reader.ReadBytes(bytes);
            var generation = Volatile.Read(ref _connectionGeneration);

            Log($"[BLE-RX] ValueChanged callback entered ConnectionGeneration={generation} ReceivedLength={bytes.Length} ReceivedBytes={ToHexPayload(bytes)}");
            if (TotalPacketsReceived == 0)
            {
                Log($"Windows native first notification received. Length={bytes.Length}.");
            }

            ProcessSensorPacket(bytes);
        }
        catch (Exception ex)
        {
            Log($"Windows native ValueChanged callback failed: {ex.Message}", ex);
        }
    }

    private void OnWindowsNativeConnectionStatusChanged(BluetoothLEDevice sender, object args)
    {
        Log($"Windows native connection status changed: {sender.ConnectionStatus}. {DescribeNativeDevice(sender)}");
        if (sender.ConnectionStatus != BluetoothConnectionStatus.Disconnected || _userDisconnectRequested)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            await _connectionGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_userDisconnectRequested)
                {
                    return;
                }

                await _sensorLog.StopAndSaveAsync(LogStopReason.UnexpectedDisconnect, Volatile.Read(ref _connectionGeneration), CancellationToken.None).ConfigureAwait(false);
                await CleanupWindowsNativeConnectionAsync(disableNotifications: false, CancellationToken.None).ConfigureAwait(false);
                SetState(BluetoothConnectionState.Disconnected);
                Log("Windows native device disconnected unexpectedly.");
                UnexpectedlyDisconnected?.Invoke(this, EventArgs.Empty);
            }
            finally
            {
                _connectionGate.Release();
            }
        });
    }

    private void OnWindowsGattSessionStatusChanged(GattSession sender, GattSessionStatusChangedEventArgs args)
        => Log($"Windows native GATT session status changed: Status={args.Status}; Error={args.Error}; MaxPduSize={sender.MaxPduSize}.");

    private static ulong ParseBluetoothAddress(string deviceId)
    {
        var addressString = deviceId.Replace(":", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);
        if (!ulong.TryParse(addressString, System.Globalization.NumberStyles.HexNumber, null, out var address))
        {
            throw new InvalidOperationException($"Invalid Bluetooth address format: {deviceId}");
        }

        return address;
    }

    private static string GetWindowsPackageContext()
    {
        try
        {
            var id = Package.Current.Id;
            return $"WindowsPackageMode=Packaged; PackageIdentity={id.FullName}; Publisher={id.Publisher};";
        }
        catch (Exception ex)
        {
            return $"WindowsPackageMode=Unpackaged; PackageIdentity=<none>; PackageQuery={ex.GetType().Name}: {ex.Message};";
        }
    }

    private string GetWindowsDeviceContext(IBluetoothRemoteDevice? device)
    {
        if (_windowsNativeDevice is not null)
        {
            var session = _windowsGattSession is null
                ? "NativeGattSession=<none>;"
                : $"NativeGattSession={_windowsGattSession.SessionStatus}; NativeMaintainConnection={_windowsGattSession.MaintainConnection}; NativeMaxPduSize={_windowsGattSession.MaxPduSize};";
            return $"{DescribeNativeDevice(_windowsNativeDevice)} {session}";
        }

        if (device is null)
        {
            return "WindowsDevice=<not selected>;";
        }

        var platformDevice = device is BluetoothRemoteDevice facade ? facade.PlatformDevice : device;
        var platformType = platformDevice.GetType();
        var gattSessionStatus = platformType.GetProperty("GattSessionStatus")?.GetValue(platformDevice);
        var bluetoothConnectionStatus = platformType.GetProperty("BluetoothConnectionStatus")?.GetValue(platformDevice);
        var bluetoothLeDeviceProxy = platformType.GetProperty("BluetoothLeDeviceProxy")?.GetValue(platformDevice);
        var nativeDevice = bluetoothLeDeviceProxy?.GetType().GetProperty("BluetoothLeDevice")?.GetValue(bluetoothLeDeviceProxy) as BluetoothLEDevice;
        if (nativeDevice is null)
        {
            return $"WindowsDevice=<native not created>; PlatformType={platformType.FullName}; GattSession={gattSessionStatus ?? "<unknown>"}; BluetoothConnection={bluetoothConnectionStatus ?? "<unknown>"};";
        }

        return $"{DescribeNativeDevice(nativeDevice)} GattSession={gattSessionStatus ?? "<unknown>"}; BluetoothConnection={bluetoothConnectionStatus ?? "<unknown>"};";
    }

    private static string DescribeNativeDevice(BluetoothLEDevice nativeDevice)
    {
        var access = nativeDevice.DeviceAccessInformation.CurrentStatus;
        var idAccess = DeviceAccessInformation.CreateFromId(nativeDevice.DeviceId).CurrentStatus;
        var pairing = nativeDevice.DeviceInformation.Pairing;
        return $"NativeDeviceId={nativeDevice.DeviceId}; NativeName={nativeDevice.Name}; DeviceAccess={access}; DeviceIdAccess={idAccess}; IsPaired={pairing.IsPaired}; CanPair={pairing.CanPair}; ProtectionLevel={pairing.ProtectionLevel}; ConnectionStatus={nativeDevice.ConnectionStatus};";
    }
#endif

    private void SetState(BluetoothConnectionState state)
    {
        State = state;
        Log($"State: {state}.");
    }

    private void Log(string message, Exception? exception = null)
    {
        if (exception is null)
        {
            _logger.LogInformation("{Message}", message);
        }
        else
        {
            _logger.LogWarning(exception, "{Message}", message);
        }

        DiagnosticMessage?.Invoke(this, $"{DateTimeOffset.Now:HH:mm:ss} {message}");
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
