using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using BLE_APP.Models;
using BLE_APP.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.Maui.ApplicationModel;
using SkiaSharp;

namespace BLE_APP.PageModels;

public sealed partial class MainPageModel : ObservableObject
{
    public const int ChartMaximumPoints = 300;
    private const string ExpectedDeviceName = "PSE84-IAQ";

    private readonly IBluetoothSensorService _bluetooth;
    private readonly ISensorLogService _sensorLog;
    private readonly int _instanceId;
    private readonly LineSeries<ObservablePoint> _pm1Series;
    private readonly LineSeries<ObservablePoint> _pm25Series;
    private readonly LineSeries<ObservablePoint> _pm4Series;
    private readonly LineSeries<ObservablePoint> _pm10Series;
    private readonly LineSeries<ObservablePoint> _vocSeries;
    private readonly LineSeries<ObservablePoint> _noxSeries;
    private readonly LineSeries<ObservablePoint> _humiditySeries;
    private readonly LineSeries<ObservablePoint> _temperatureSeries;
    private readonly LineSeries<ObservablePoint> _co2Series;
    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _connectionCts;
    private DateTimeOffset _lastUiReadingUpdate = DateTimeOffset.MinValue;
    private DateTimeOffset? _chartStartTimestamp;
    private ulong _readingReceivedCount;
    private ManualLabelState? _pendingManualLabel;
    private CancellationTokenSource? _manualLabelConfirmationCts;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    private DiscoveredSensorDevice? _selectedDevice;

    [ObservableProperty]
    private string _searchText = ExpectedDeviceName;

    [ObservableProperty]
    private string _statusText = "Idle";

    [ObservableProperty]
    private string _deviceName = "--";

    [ObservableProperty]
    private string _latestUpdateText = "--";

    [ObservableProperty]
    private string _packetText = "Packets: 0";

    [ObservableProperty]
    private string _manualLabelText = "Waiting";

    [ObservableProperty]
    private string _manualLabelPendingText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SetSmokingCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetNoSmokingCommand))]
    private bool _isManualLabelCommandPending;

    [ObservableProperty]
    private string _loggingStatusText = "Logging disabled: select a valid log folder.";

    [ObservableProperty]
    private string _logFolderDisplayText = "Not selected";

    [ObservableProperty]
    private string _errorText = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private BluetoothConnectionState _connectionState = BluetoothConnectionState.Idle;

    [ObservableProperty]
    private bool _hasReading;

    [ObservableProperty]
    private bool _isPm1Visible = true;

    [ObservableProperty]
    private bool _isPm25Visible = true;

    [ObservableProperty]
    private bool _isPm4Visible = true;

    [ObservableProperty]
    private bool _isPm10Visible = true;

    [ObservableProperty]
    private bool _isVocVisible = true;

    [ObservableProperty]
    private bool _isNoxVisible = true;

    public MainPageModel(IBluetoothSensorService bluetooth, ISensorLogService sensorLog)
    {
        _bluetooth = bluetooth;
        _sensorLog = sensorLog;
        _instanceId = RuntimeHelpers.GetHashCode(this);
        Debug.WriteLine($"[CHART] MainViewModel constructed InstanceId={_instanceId}");
        _bluetooth.DeviceDiscovered += OnDeviceDiscovered;
        _bluetooth.ReadingReceived += OnReadingReceived;
        _bluetooth.UnexpectedlyDisconnected += OnUnexpectedlyDisconnected;
        _sensorLog.StatusChanged += OnLogStatusChanged;
        LoggingStatusText = _sensorLog.StatusText;
        LogFolderDisplayText = _sensorLog.LogFolderDisplayText;

        Metrics =
        [
            new SensorMetric("PM1.0", "ug/m3"),
            new SensorMetric("PM2.5", "ug/m3"),
            new SensorMetric("PM4.0", "ug/m3"),
            new SensorMetric("PM10.0", "ug/m3"),
            new SensorMetric("Humidity", "%RH"),
            new SensorMetric("Temperature", "degC"),
            new SensorMetric("NOx", "index"),
            new SensorMetric("VOC", "index"),
            new SensorMetric("CO2", "ppm"),
            new SensorMetric("Distance", "m")
        ];

        _pm1Series = CreateSeries("PM1.0", Pm1Values, SKColors.DodgerBlue);
        _pm25Series = CreateSeries("PM2.5", Pm25Values, SKColors.SeaGreen);
        _pm4Series = CreateSeries("PM4.0", Pm4Values, SKColors.DarkOrange);
        _pm10Series = CreateSeries("PM10.0", Pm10Values, SKColors.Crimson);
        _vocSeries = CreateSeries("VOC", VocValues, SKColors.MediumPurple);
        _noxSeries = CreateSeries("NOx", NoxValues, SKColors.Teal);
        _humiditySeries = CreateSeries("Humidity", HumidityValues, SKColors.RoyalBlue);
        _temperatureSeries = CreateSeries("Temperature", TemperatureValues, SKColors.OrangeRed);
        _co2Series = CreateSeries("CO2", Co2Values, SKColors.ForestGreen);

        PmSeries = [_pm1Series, _pm25Series, _pm4Series, _pm10Series];
        VocNoxSeries = [_vocSeries, _noxSeries];
        HumiditySeries = [_humiditySeries];
        TemperatureSeries = [_temperatureSeries];
        Co2Series = [_co2Series];

        Debug.WriteLine($"[CHART] ViewModel series count={PmSeries.Length}");
        Debug.WriteLine($"[CHART] Rolling capacity={ChartMaximumPoints}");
    }

    public ObservableCollection<DiscoveredSensorDevice> Devices { get; } = [];

    public ObservableCollection<SensorMetric> Metrics { get; }

    public ObservableCollection<ObservablePoint> Pm1Values { get; } = [];

    public ObservableCollection<ObservablePoint> Pm25Values { get; } = [];

    public ObservableCollection<ObservablePoint> Pm4Values { get; } = [];

    public ObservableCollection<ObservablePoint> Pm10Values { get; } = [];

    public ObservableCollection<ObservablePoint> VocValues { get; } = [];

    public ObservableCollection<ObservablePoint> NoxValues { get; } = [];

    public ObservableCollection<ObservablePoint> HumidityValues { get; } = [];

    public ObservableCollection<ObservablePoint> TemperatureValues { get; } = [];

    public ObservableCollection<ObservablePoint> Co2Values { get; } = [];

    public ISeries[] PmSeries { get; }

    public ISeries[] VocNoxSeries { get; }

    public ISeries[] HumiditySeries { get; }

    public ISeries[] TemperatureSeries { get; }

    public ISeries[] Co2Series { get; }

    public Axis[] PmXAxes { get; } = CreateElapsedSecondsAxes();

    public Axis[] PmYAxes { get; } = CreateZeroMinAxes();

    public Axis[] VocNoxXAxes { get; } = CreateElapsedSecondsAxes();

    public Axis[] VocNoxYAxes { get; } = CreateZeroMinAxes();

    public Axis[] HumidityXAxes { get; } = CreateElapsedSecondsAxes();

    public Axis[] HumidityYAxes { get; } = CreateZeroMinAxes();

    public Axis[] TemperatureXAxes { get; } = CreateElapsedSecondsAxes();

    public Axis[] TemperatureYAxes { get; } = CreateZeroMinAxes();

    public Axis[] Co2XAxes { get; } = CreateElapsedSecondsAxes();

    public Axis[] Co2YAxes { get; } = CreateZeroMinAxes();

    public Axis[] ZeroMinYAxis => PmYAxes;

    public bool IsWaitingForReading => !HasReading;

    public bool CanConnect => SelectedDevice is not null && !IsBusy;

    public bool CanScan => !IsBusy && !IsScanning;

    public bool CanSetManualLabel => _bluetooth.CanSetManualLabel && !IsBusy && !IsManualLabelCommandPending;

    public bool Pm1ValuesReferenceMatches => ReferenceEquals(_pm1Series.Values, Pm1Values);

    public bool Pm1SeriesReferenceMatches => ReferenceEquals(PmSeries[0], _pm1Series);

    partial void OnHasReadingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsWaitingForReading));
    }

    partial void OnIsBusyChanged(bool value)
    {
        ScanCommand.NotifyCanExecuteChanged();
        ConnectCommand.NotifyCanExecuteChanged();
        DisconnectCommand.NotifyCanExecuteChanged();
        ReconnectCommand.NotifyCanExecuteChanged();
        SetSmokingCommand.NotifyCanExecuteChanged();
        SetNoSmokingCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsManualLabelCommandPendingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanSetManualLabel));
    }

    partial void OnIsPm1VisibleChanged(bool value) => SetSeriesVisibility(_pm1Series, value);

    partial void OnIsPm25VisibleChanged(bool value) => SetSeriesVisibility(_pm25Series, value);

    partial void OnIsPm4VisibleChanged(bool value) => SetSeriesVisibility(_pm4Series, value);

    partial void OnIsPm10VisibleChanged(bool value) => SetSeriesVisibility(_pm10Series, value);

    partial void OnIsVocVisibleChanged(bool value) => SetSeriesVisibility(_vocSeries, value);

    partial void OnIsNoxVisibleChanged(bool value) => SetSeriesVisibility(_noxSeries, value);

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task Scan()
    {
        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _scanCts = new CancellationTokenSource();

        try
        {
            IsBusy = true;
            IsScanning = true;
            ErrorText = string.Empty;
            Devices.Clear();
            SelectedDevice = null;
            UpdateState(BluetoothConnectionState.Scanning);

            var devices = await _bluetooth.ScanAsync(SearchText, TimeSpan.FromSeconds(8), _scanCts.Token);
            foreach (var device in devices)
            {
                UpsertDevice(device);
            }

            if (Devices.Count == 0)
            {
                ErrorText = "PSE84-IAQ was not found. Check power, advertising, and Bluetooth permissions.";
            }
        }
        catch (OperationCanceledException)
        {
            ErrorText = "Scan cancelled.";
        }
        catch (Exception ex)
        {
            ErrorText = $"Scan failed: {ex.Message}";
            UpdateState(BluetoothConnectionState.Error);
        }
        finally
        {
            IsScanning = false;
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CancelScan()
    {
        try
        {
            _scanCts?.Cancel();
            await _bluetooth.StopScanAsync(CancellationToken.None);
            ErrorText = "Scan cancelled.";
        }
        catch (Exception ex)
        {
            ErrorText = $"Cancel scan failed: {ex.Message}";
            UpdateState(BluetoothConnectionState.Error);
        }
        finally
        {
            IsScanning = false;
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task Connect()
    {
        if (SelectedDevice is null)
        {
            return;
        }

        _connectionCts?.Cancel();
        _connectionCts?.Dispose();
        _connectionCts = new CancellationTokenSource();
        _scanCts?.Cancel();
        ClearManualLabelPending();

        try
        {
            IsBusy = true;
            IsScanning = false;
            ErrorText = string.Empty;
            DeviceName = SelectedDevice.Name;
            await _bluetooth.ConnectAsync(SelectedDevice.Id, _connectionCts.Token);
            UpdateState(_bluetooth.State);
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message.StartsWith("Connected, but", StringComparison.OrdinalIgnoreCase)
                ? ex.Message
                : $"Connection failed: {ex.Message}";
            UpdateState(BluetoothConnectionState.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task Disconnect()
    {
        try
        {
            IsBusy = true;
            _connectionCts?.Cancel();
            await _bluetooth.DisconnectAsync(userInitiated: true, CancellationToken.None);
            ClearManualLabelPending();
            UpdateState(BluetoothConnectionState.Disconnected);
        }
        catch (Exception ex)
        {
            ErrorText = $"Disconnect failed: {ex.Message}";
            UpdateState(BluetoothConnectionState.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task Reconnect()
    {
        _connectionCts?.Cancel();
        _connectionCts?.Dispose();
        _connectionCts = new CancellationTokenSource();
        ClearManualLabelPending();

        try
        {
            IsBusy = true;
            ErrorText = string.Empty;
            await _bluetooth.ReconnectAsync(_connectionCts.Token);
            UpdateState(_bluetooth.State);
        }
        catch (Exception ex)
        {
            ErrorText = $"Reconnect failed: {ex.Message}";
            UpdateState(BluetoothConnectionState.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSetManualLabel))]
    private Task SetSmoking()
        => SetManualLabel(ManualLabelState.Smoking);

    [RelayCommand(CanExecute = nameof(CanSetManualLabel))]
    private Task SetNoSmoking()
        => SetManualLabel(ManualLabelState.NoSmoking);

    [RelayCommand]
    private async Task SelectLogFolder()
    {
        try
        {
            await _sensorLog.SelectPublicFolderAsync(CancellationToken.None);
            LoggingStatusText = _sensorLog.StatusText;
            LogFolderDisplayText = _sensorLog.LogFolderDisplayText;
        }
        catch (Exception ex)
        {
            ErrorText = $"Log folder setup failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private Task Appearing()
    {
        Debug.WriteLine($"[CHART] MainViewModel appearing InstanceId={_instanceId}");
        UpdateState(_bluetooth.State);
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task Disappearing()
    {
        try
        {
            _scanCts?.Cancel();
            await _bluetooth.StopScanAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[BLE-SCAN] Stop scan during disappearing failed: {ex}");
        }
        finally
        {
            IsScanning = false;
        }
    }

    public void AddChartReadingForTest(SensorReading reading)
    {
        AddChartReading(reading);
    }

    private void OnDeviceDiscovered(object? sender, DiscoveredSensorDevice device)
        => MainThread.BeginInvokeOnMainThread(() => UpsertDevice(device));

    private void OnReadingReceived(object? sender, SensorReading reading)
    {
        var now = DateTimeOffset.Now;
        _readingReceivedCount++;
        if (_readingReceivedCount == 1 || _readingReceivedCount % 100 == 0)
        {
            Debug.WriteLine("[CHART] MainViewModel received reading");
            Debug.WriteLine($"[CHART] InstanceId={_instanceId}");
            Debug.WriteLine($"[CHART] Timestamp={reading.Timestamp:O}");
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            AddChartReading(reading);
            HasReading = true;

            if ((now - _lastUiReadingUpdate) < TimeSpan.FromMilliseconds(150))
            {
                return;
            }

            _lastUiReadingUpdate = now;
            UpdateState(BluetoothConnectionState.ReceivingData);
            LatestUpdateText = reading.Timestamp.ToLocalTime().ToString("HH:mm:ss");
            PacketText = $"Packets: {_bluetooth.TotalPacketsReceived}  Malformed: {_bluetooth.MalformedPacketsReceived}  Gaps: {_bluetooth.SequenceGapsDetected}  Seq: {reading.SequenceNumber}";

            SetMetric("PM1.0", Format(reading.Pm1, "0.0"));
            SetMetric("PM2.5", Format(reading.Pm25, "0.0"));
            SetMetric("PM4.0", Format(reading.Pm4, "0.0"));
            SetMetric("PM10.0", Format(reading.Pm10, "0.0"));
            SetMetric("Humidity", Format(reading.Humidity, "0.00"));
            SetMetric("Temperature", Format(reading.Temperature, "0.00"));
            SetMetric("NOx", Format(reading.Nox, "0.0"));
            SetMetric("VOC", Format(reading.Voc, "0.0"));
            SetMetric("CO2", reading.Co2?.ToString() ?? "--");
            SetMetric("Distance", reading.DistanceMetres.ToString("0.###"));
            UpdateManualLabel(reading.ManualLabel);
        });
    }

    private async Task SetManualLabel(ManualLabelState requestedLabel)
    {
        _manualLabelConfirmationCts?.Cancel();
        _manualLabelConfirmationCts?.Dispose();
        _manualLabelConfirmationCts = new CancellationTokenSource();

        try
        {
            IsManualLabelCommandPending = true;
            _pendingManualLabel = requestedLabel;
            ManualLabelPendingText = $"Pending: {SensorPacketProtocol.FormatManualLabel(requestedLabel)}";
            await _bluetooth.SetManualLabelAsync(requestedLabel, _manualLabelConfirmationCts.Token);
            _ = WatchManualLabelConfirmationAsync(requestedLabel, _manualLabelConfirmationCts.Token);
        }
        catch (Exception ex)
        {
            IsManualLabelCommandPending = false;
            _pendingManualLabel = null;
            ManualLabelPendingText = string.Empty;
            ErrorText = $"Manual label command failed: {ex.Message}";
        }
    }

    private async Task WatchManualLabelConfirmationAsync(ManualLabelState requestedLabel, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            if (_pendingManualLabel == requestedLabel)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    IsManualLabelCommandPending = false;
                    _pendingManualLabel = null;
                    ManualLabelPendingText = string.Empty;
                    ErrorText = $"Manual label confirmation timed out for {SensorPacketProtocol.FormatManualLabel(requestedLabel)}.";
                });
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void UpdateManualLabel(ManualLabelState reportedLabel)
    {
        ManualLabelText = SensorPacketProtocol.FormatManualLabel(reportedLabel);
        if (_pendingManualLabel == reportedLabel)
        {
            _manualLabelConfirmationCts?.Cancel();
            IsManualLabelCommandPending = false;
            _pendingManualLabel = null;
            ManualLabelPendingText = string.Empty;
        }
    }

    private void AddChartReading(SensorReading reading)
    {
        _chartStartTimestamp ??= reading.Timestamp;
        var elapsedSeconds = Math.Max(0, (reading.Timestamp - _chartStartTimestamp.Value).TotalSeconds);

        Debug.WriteLine($"[CHART] Update thread main={IsMainThreadForDiagnostics()}");
        AddPoint(Pm1Values, elapsedSeconds, reading.Pm1);
        AddPoint(Pm25Values, elapsedSeconds, reading.Pm25);
        AddPoint(Pm4Values, elapsedSeconds, reading.Pm4);
        AddPoint(Pm10Values, elapsedSeconds, reading.Pm10);
        AddPoint(VocValues, elapsedSeconds, reading.Voc);
        AddPoint(NoxValues, elapsedSeconds, reading.Nox);
        AddPoint(HumidityValues, elapsedSeconds, reading.Humidity);
        AddPoint(TemperatureValues, elapsedSeconds, reading.Temperature);
        AddPoint(Co2Values, elapsedSeconds, reading.Co2.HasValue ? reading.Co2.Value : null);
        TrimChartCollections();
#if ANDROID
        Debug.WriteLine($"[ANDROID-CHART] Reading applied on main thread={IsMainThreadForDiagnostics()}");
#endif

        if (ShouldLogChartCount(Pm1Values.Count))
        {
            Debug.WriteLine($"[CHART] PM1 point X={elapsedSeconds:0.###} Y={reading.Pm1?.ToString("0.###") ?? "<null>"}");
            Debug.WriteLine($"[CHART] PM1 count={Pm1Values.Count}");
            Debug.WriteLine($"[CHART] Current aligned point count={Pm1Values.Count}");
            Debug.WriteLine($"[CHART] Values reference matches={Pm1ValuesReferenceMatches}");
            Debug.WriteLine($"[CHART] Series reference matches={Pm1SeriesReferenceMatches}");
        }
    }

    private void OnLogStatusChanged(object? sender, string status)
        => MainThread.BeginInvokeOnMainThread(() =>
        {
            LoggingStatusText = status;
            LogFolderDisplayText = _sensorLog.LogFolderDisplayText;
        });

    private async void OnUnexpectedlyDisconnected(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ErrorText = "Device disconnected unexpectedly. Reconnect will retry automatically.";
            ClearManualLabelPending();
            UpdateState(BluetoothConnectionState.Disconnected);
        });

        try
        {
            await _bluetooth.ReconnectAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                ErrorText = $"Automatic reconnect failed: {ex.Message}";
                UpdateState(BluetoothConnectionState.Disconnected);
            });
        }
    }

    private void UpsertDevice(DiscoveredSensorDevice device)
    {
        var existing = Devices.FirstOrDefault(item => item.Id == device.Id);
        if (existing is not null)
        {
            var index = Devices.IndexOf(existing);
            Devices[index] = device;
        }
        else
        {
            Devices.Add(device);
        }

        SelectedDevice ??= Devices.FirstOrDefault(item => item.AdvertisesExpectedService)
                           ?? Devices.FirstOrDefault(item => item.Name.Contains(ExpectedDeviceName, StringComparison.OrdinalIgnoreCase));
    }

    private void UpdateState(BluetoothConnectionState state)
    {
        ConnectionState = state;
        StatusText = state.ToString();
        if (state is BluetoothConnectionState.Disconnected or BluetoothConnectionState.Idle or BluetoothConnectionState.Error)
        {
            ClearManualLabelPending();
        }

        OnPropertyChanged(nameof(CanSetManualLabel));
        SetSmokingCommand.NotifyCanExecuteChanged();
        SetNoSmokingCommand.NotifyCanExecuteChanged();
    }

    private void ClearManualLabelPending()
    {
        _manualLabelConfirmationCts?.Cancel();
        IsManualLabelCommandPending = false;
        _pendingManualLabel = null;
        ManualLabelPendingText = string.Empty;
    }

    private void SetMetric(string name, string value)
    {
        var metric = Metrics.First(item => item.Name == name);
        metric.Value = value;
    }

    private static LineSeries<ObservablePoint> CreateSeries(
        string name,
        ObservableCollection<ObservablePoint> values,
        SKColor color)
        => new()
        {
            Name = name,
            Values = values,
            Stroke = new SolidColorPaint(color, 2),
            GeometryStroke = new SolidColorPaint(color, 2),
            GeometryFill = new SolidColorPaint(SKColors.White),
            GeometrySize = 7,
            Fill = null,
            LineSmoothness = 0
        };

    private static void AddPoint(
        ObservableCollection<ObservablePoint> values,
        double elapsedSeconds,
        double? value,
        [CallerArgumentExpression(nameof(values))] string fieldName = "")
    {
        if (!double.IsFinite(elapsedSeconds))
        {
            Debug.WriteLine($"[CHART] Rejected point for {fieldName}: non-finite X={elapsedSeconds}");
            return;
        }

        if (value.HasValue && !double.IsFinite(value.Value))
        {
            Debug.WriteLine($"[CHART] Rejected point for {fieldName}: non-finite Y={value.Value}");
            values.Add(new ObservablePoint(elapsedSeconds, null));
            return;
        }

        values.Add(new ObservablePoint(elapsedSeconds, value));
    }

    private static Axis[] CreateElapsedSecondsAxes()
        => [new Axis { Labeler = value => $"{value:0}s" }];

    private static Axis[] CreateZeroMinAxes()
        => [new Axis { MinLimit = 0, MaxLimit = null }];

    private void TrimChartCollections()
    {
        foreach (var values in AllChartValueCollections())
        {
            while (values.Count > ChartMaximumPoints)
            {
                values.RemoveAt(0);
            }
        }
    }

    private IEnumerable<ObservableCollection<ObservablePoint>> AllChartValueCollections()
    {
        yield return Pm1Values;
        yield return Pm25Values;
        yield return Pm4Values;
        yield return Pm10Values;
        yield return VocValues;
        yield return NoxValues;
        yield return HumidityValues;
        yield return TemperatureValues;
        yield return Co2Values;
    }

    private static void SetSeriesVisibility(LineSeries<ObservablePoint> series, bool isVisible)
    {
        series.IsVisible = isVisible;
        Debug.WriteLine($"[CHART] Series visibility changed: {series.Name}={isVisible}");
    }

    private static bool ShouldLogChartCount(int count)
        => count is 1 or 2 or 5 or 10 or 50 or 100;

    private static string Format(double? value, string format)
        => value.HasValue ? value.Value.ToString(format) : "--";

    private static bool IsMainThreadForDiagnostics()
    {
#if ANDROID || IOS || MACCATALYST || WINDOWS
        return Microsoft.Maui.ApplicationModel.MainThread.IsMainThread;
#else
        return true;
#endif
    }
}
