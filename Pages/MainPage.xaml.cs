using System.Diagnostics;
using System.Collections;
using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using BLE_APP.PageModels;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView.Maui;

namespace BLE_APP.Pages
{
    public partial class MainPage : ContentPage
    {
        private const double NarrowLayoutBreakpoint = 760;
        private const double SingleMetricColumnBreakpoint = 390;
        private ResponsiveLayoutMode? _activeLayoutMode;
        private int _activeMetricSpan;

        public MainPage(MainPageModel model)
        {
#if ANDROID
            Debug.WriteLine("[ANDROID-STARTUP] MainPage constructor entered");
#endif
            Debug.WriteLine("[STARTUP] MainPage constructor entered");
            Debug.WriteLine("[STARTUP] MainPage InitializeComponent starting");
            InitializeComponent();
#if ANDROID
            Debug.WriteLine("[ANDROID-STARTUP] MainPage InitializeComponent completed");
#endif
            Debug.WriteLine("[STARTUP] MainPage InitializeComponent completed");
            BindingContext = model;
            Debug.WriteLine("[STARTUP] MainPage BindingContext assigned");
            Debug.WriteLine($"[CHART] MainPage BindingContext InstanceId={RuntimeHelpers.GetHashCode(model)}");
            model.Pm1Values.CollectionChanged += OnPm1ValuesCollectionChanged;
            SizeChanged += OnSizeChanged;
            Loaded += OnLoaded;
        }

        private void OnSizeChanged(object? sender, EventArgs e)
        {
            ApplyResponsiveLayout(Width);
        }

        private void OnLoaded(object? sender, EventArgs e)
        {
#if ANDROID
            Debug.WriteLine("[ANDROID-STARTUP] MainPage Loaded");
#endif
            Debug.WriteLine("[STARTUP] MainPage Loaded entered");
            ApplyResponsiveLayout(Width);
            Debug.WriteLine($"[CHART] Graph panel measured width={GraphPanel.Width:0.#} height={GraphPanel.Height:0.#} visible={GraphPanel.IsVisible}");
            Debug.WriteLine("[STARTUP] MainPage Loaded completed");
        }

        private void ApplyResponsiveLayout(double pageWidth)
        {
            if (pageWidth <= 0)
            {
                return;
            }

            var mode = pageWidth < NarrowLayoutBreakpoint
                ? ResponsiveLayoutMode.Narrow
                : ResponsiveLayoutMode.Wide;
            var metricSpan = mode == ResponsiveLayoutMode.Wide
                ? 3
                : pageWidth < SingleMetricColumnBreakpoint ? 1 : 2;

            if (_activeLayoutMode == mode && _activeMetricSpan == metricSpan)
            {
                return;
            }

            _activeLayoutMode = mode;
            _activeMetricSpan = metricSpan;
            MetricItemsLayout.Span = metricSpan;

            if (mode == ResponsiveLayoutMode.Narrow)
            {
                ApplyNarrowLayout();
            }
            else
            {
                ApplyWideLayout();
            }

            Debug.WriteLine($"[LAYOUT] mode={mode} width={pageWidth:0.#} metricSpan={metricSpan}");
        }

        private void ApplyNarrowLayout()
        {
            PageGrid.Padding = new Thickness(12);

            SetRows(BleHeaderGrid, GridLength.Auto, GridLength.Auto);
            SetColumns(BleHeaderGrid, GridLength.Star);
            Grid.SetRow(ConnectionStateLabel, 1);
            Grid.SetColumn(ConnectionStateLabel, 0);
            ConnectionStateLabel.HorizontalTextAlignment = TextAlignment.Start;

            SetRows(BleSearchGrid, GridLength.Auto, GridLength.Auto, GridLength.Auto);
            SetColumns(BleSearchGrid, GridLength.Star);
            Grid.SetRow(SearchDevicesBar, 0);
            Grid.SetColumn(SearchDevicesBar, 0);
            Grid.SetRow(ScanButton, 1);
            Grid.SetColumn(ScanButton, 0);
            Grid.SetRow(CancelScanButton, 2);
            Grid.SetColumn(CancelScanButton, 0);

            SetRows(BleDeviceGrid, GridLength.Auto, GridLength.Auto, GridLength.Auto);
            SetColumns(BleDeviceGrid, GridLength.Star);
            Grid.SetRow(DeviceList, 0);
            Grid.SetColumn(DeviceList, 0);
            Grid.SetRow(ConnectButton, 1);
            Grid.SetColumn(ConnectButton, 0);
            Grid.SetRow(DisconnectButtonStack, 2);
            Grid.SetColumn(DisconnectButtonStack, 0);

            SetRows(LoggingSectionGrid, GridLength.Auto, GridLength.Auto, GridLength.Auto);
            SetColumns(LoggingSectionGrid, GridLength.Star);
            Grid.SetRow(SelectFolderButton, 1);
            Grid.SetColumn(SelectFolderButton, 0);
            Grid.SetRowSpan(SelectFolderButton, 1);
            var loggingStatus = (View)LoggingSectionGrid.Children[2];
            Grid.SetRowSpan(loggingStatus, 1);
            Grid.SetRow(loggingStatus, 2);
            Grid.SetColumn(loggingStatus, 0);
            Grid.SetColumnSpan(loggingStatus, 1);

            SetRows(ChartGrid, GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto);
            SetColumns(ChartGrid, GridLength.Star);
            PlaceChart(PmChartCard, 0, 0);
            PlaceChart(VocNoxChartCard, 1, 0);
            PlaceChart(HumidityChartCard, 2, 0);
            PlaceChart(TemperatureChartCard, 3, 0);
            PlaceChart(Co2ChartCard, 4, 0);
            SetChartHeight(300);
        }

        private void ApplyWideLayout()
        {
            PageGrid.Padding = new Thickness(24);

            SetRows(BleHeaderGrid, GridLength.Auto);
            SetColumns(BleHeaderGrid, GridLength.Star, GridLength.Auto);
            Grid.SetRow(ConnectionStateLabel, 0);
            Grid.SetColumn(ConnectionStateLabel, 1);
            ConnectionStateLabel.HorizontalTextAlignment = TextAlignment.End;

            SetRows(BleSearchGrid, GridLength.Auto);
            SetColumns(BleSearchGrid, GridLength.Star, GridLength.Auto, GridLength.Auto);
            Grid.SetRow(SearchDevicesBar, 0);
            Grid.SetColumn(SearchDevicesBar, 0);
            Grid.SetRow(ScanButton, 0);
            Grid.SetColumn(ScanButton, 1);
            Grid.SetRow(CancelScanButton, 0);
            Grid.SetColumn(CancelScanButton, 2);

            SetRows(BleDeviceGrid, GridLength.Auto);
            SetColumns(BleDeviceGrid, GridLength.Star, GridLength.Auto, GridLength.Auto);
            Grid.SetRow(DeviceList, 0);
            Grid.SetColumn(DeviceList, 0);
            Grid.SetRow(ConnectButton, 0);
            Grid.SetColumn(ConnectButton, 1);
            Grid.SetRow(DisconnectButtonStack, 0);
            Grid.SetColumn(DisconnectButtonStack, 2);

            SetRows(LoggingSectionGrid, GridLength.Auto, GridLength.Auto);
            SetColumns(LoggingSectionGrid, GridLength.Star, GridLength.Auto);
            Grid.SetRow(SelectFolderButton, 0);
            Grid.SetColumn(SelectFolderButton, 1);
            var loggingStatus = (View)LoggingSectionGrid.Children[2];
            Grid.SetRow(loggingStatus, 1);
            Grid.SetColumn(loggingStatus, 0);
            Grid.SetColumnSpan(loggingStatus, 2);

            SetRows(ChartGrid, GridLength.Auto, GridLength.Auto, GridLength.Auto);
            SetColumns(ChartGrid, GridLength.Star, GridLength.Star);
            PlaceChart(PmChartCard, 0, 0);
            PlaceChart(VocNoxChartCard, 0, 1);
            PlaceChart(HumidityChartCard, 1, 0);
            PlaceChart(TemperatureChartCard, 1, 1);
            PlaceChart(Co2ChartCard, 2, 0);
            SetChartHeight(250);
        }

        private static void SetRows(Grid grid, params GridLength[] heights)
        {
            grid.RowDefinitions.Clear();
            foreach (var height in heights)
            {
                grid.RowDefinitions.Add(new RowDefinition(height));
            }
        }

        private static void SetColumns(Grid grid, params GridLength[] widths)
        {
            grid.ColumnDefinitions.Clear();
            foreach (var width in widths)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition(width));
            }
        }

        private static void PlaceChart(View chartCard, int row, int column)
        {
            Grid.SetRow(chartCard, row);
            Grid.SetColumn(chartCard, column);
        }

        private void SetChartHeight(double height)
        {
            foreach (var chart in new[] { PmChart, VocNoxChart, HumidityChart, TemperatureChart, Co2Chart })
            {
                chart.MinimumHeightRequest = height;
                chart.HeightRequest = height;
                chart.HorizontalOptions = LayoutOptions.Fill;
            }
        }

        private void OnLiveChartHandlerChanged(object? sender, EventArgs e)
        {
            if (sender is not CartesianChart chart)
            {
                return;
            }

            LogLiveChart($"{GetChartName(chart)} HandlerChanged handler={chart.Handler?.GetType().FullName ?? "<null>"}");
        }

        private void OnLiveChartLoaded(object? sender, EventArgs e)
        {
            if (sender is not CartesianChart chart)
            {
                return;
            }

            LogLiveChartInspection(chart, "Loaded");
            LogLiveChartReferenceChecks(chart);
        }

        private void OnLiveChartSizeChanged(object? sender, EventArgs e)
        {
            if (sender is not CartesianChart chart)
            {
                return;
            }

            LogLiveChartSize(chart);
        }

        private void OnPm1ValuesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (BindingContext is not MainPageModel model || !ShouldLogLiveChartCount(model.Pm1Values.Count))
            {
                return;
            }

            MainThread.BeginInvokeOnMainThread(() => LogLiveChartReferenceChecks(PmChart));
        }

        private string GetChartName(CartesianChart chart)
        {
            if (ReferenceEquals(chart, PmChart))
            {
                return "PmChart";
            }

            if (ReferenceEquals(chart, VocNoxChart))
            {
                return "VocNoxChart";
            }

            if (ReferenceEquals(chart, HumidityChart))
            {
                return "HumidityChart";
            }

            if (ReferenceEquals(chart, TemperatureChart))
            {
                return "TemperatureChart";
            }

            if (ReferenceEquals(chart, Co2Chart))
            {
                return "Co2Chart";
            }

            return chart.GetType().Name;
        }

        private void LogLiveChartSize(CartesianChart chart)
        {
            var name = GetChartName(chart);
            LogLiveChart($"{name} size width={chart.Width:0.#} height={chart.Height:0.#}");
            LogLiveChart($"{name} visible={chart.IsVisible} opacity={chart.Opacity:0.##} parent={chart.Parent?.GetType().Name ?? "<null>"}");
        }

        private void LogLiveChartInspection(CartesianChart chart, string eventName)
        {
            var name = GetChartName(chart);
            var series = chart.Series?.ToArray() ?? [];
            LogLiveChart($"{name} {eventName}");
            LogLiveChartSize(chart);
            LogLiveChart($"{name} BindingContext={chart.BindingContext?.GetType().FullName ?? "<null>"}");
            LogLiveChart($"{name} Series count={series.Length}");
            LogLiveChart($"{name} XAxes count={(chart.XAxes?.Count() ?? 0)} YAxes count={(chart.YAxes?.Count() ?? 0)}");

            foreach (var item in series)
            {
                var values = GetSeriesValues(item);
                var valuesCount = values?.Cast<object>().Count() ?? 0;
                LogLiveChart($"{name} series name={item.Name ?? "<null>"} type={item.GetType().FullName}");
                LogLiveChart($"{name} {item.Name ?? "<unnamed>"} Values type={values?.GetType().FullName ?? "<null>"} count={valuesCount}");
            }
        }

        private void LogLiveChartReferenceChecks(CartesianChart chart)
        {
            if (BindingContext is not MainPageModel model)
            {
                return;
            }

            var chartName = GetChartName(chart);
            var series = chart.Series?.ToArray() ?? [];
            switch (chartName)
            {
                case "PmChart":
                    LogReferenceCheck(chartName, "PM1", series, model.PmSeries, model.Pm1Values);
                    break;
                case "VocNoxChart":
                    LogReferenceCheck(chartName, "VOC", series, model.VocNoxSeries, model.VocValues);
                    break;
                case "HumidityChart":
                    LogReferenceCheck(chartName, "Humidity", series, model.HumiditySeries, model.HumidityValues);
                    break;
                case "TemperatureChart":
                    LogReferenceCheck(chartName, "Temperature", series, model.TemperatureSeries, model.TemperatureValues);
                    break;
                case "Co2Chart":
                    LogReferenceCheck(chartName, "CO2", series, model.Co2Series, model.Co2Values);
                    break;
            }
        }

        private static void LogReferenceCheck(
            string chartName,
            string seriesName,
            ISeries[] chartSeries,
            ISeries[] modelSeries,
            IEnumerable modelValues)
        {
            var firstChartSeries = chartSeries.FirstOrDefault();
            var firstModelSeries = modelSeries.FirstOrDefault();
            var chartValues = firstChartSeries is null ? null : GetSeriesValues(firstChartSeries);
            var valuesCount = chartValues?.Cast<object>().Count() ?? 0;

            LogLiveChart($"{chartName} {seriesName} series reference matches={ReferenceEquals(firstChartSeries, firstModelSeries)}");
            LogLiveChart($"{chartName} {seriesName} values reference matches={ReferenceEquals(chartValues, modelValues)}");
            LogLiveChart($"{chartName} {seriesName} values count={valuesCount}");
        }

        private static IEnumerable? GetSeriesValues(ISeries series)
            => series.GetType().GetProperty("Values")?.GetValue(series) as IEnumerable;

        private static void LogLiveChart(string message)
        {
            StartupDiagnostics.Log($"[LIVE-CHART] {message}");
        }

        private static bool ShouldLogLiveChartCount(int count)
            => count is 1 or 2 or 5 or 10 or 50 or 100;

        private enum ResponsiveLayoutMode
        {
            Narrow,
            Wide
        }
    }
}
