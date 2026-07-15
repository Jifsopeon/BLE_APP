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
            Debug.WriteLine($"[CHART] MainPage size width={Width:0.#} height={Height:0.#}");
            Debug.WriteLine($"[CHART] Graph panel measured width={GraphPanel.Width:0.#} height={GraphPanel.Height:0.#} visible={GraphPanel.IsVisible}");
        }

        private void OnLoaded(object? sender, EventArgs e)
        {
#if ANDROID
            Debug.WriteLine("[ANDROID-STARTUP] MainPage Loaded");
#endif
            Debug.WriteLine("[STARTUP] MainPage Loaded entered");
            Debug.WriteLine($"[CHART] Graph panel measured width={GraphPanel.Width:0.#} height={GraphPanel.Height:0.#} visible={GraphPanel.IsVisible}");
            Debug.WriteLine("[STARTUP] MainPage Loaded completed");
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
    }
}
