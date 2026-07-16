using System.Diagnostics;
using System.Runtime.ExceptionServices;
using BLE_APP.Services;
using Bluetooth.Maui;
using CommunityToolkit.Maui;
using LiveChartsCore.SkiaSharpView.Maui;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Hosting;
using SkiaSharp.Views.Maui.Controls.Hosting;
using SkiaSharp.Views.Maui.Handlers;

namespace BLE_APP
{
    public static class MauiProgram
    {
#if DEBUG && WINDOWS
        private static bool s_firstChanceDiagnosticsAttached;
#endif

        public static MauiApp CreateMauiApp()
        {
#if ANDROID
            Debug.WriteLine("[ANDROID-STARTUP] MauiProgram.CreateMauiApp entered");
#endif
#if DEBUG
            StartupDiagnostics.Reset();
#if WINDOWS
            AttachWindowsFirstChanceDiagnostics();
#endif
#endif
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .UseMauiCommunityToolkit()
                .UseSkiaSharp()
                .UseLiveCharts()
                .ConfigureMauiHandlers(handlers =>
                {
                    handlers.TryAddHandler(GetLiveChartsRenderModeType("CPURenderMode"), typeof(SKCanvasViewHandler));
                    handlers.TryAddHandler(GetLiveChartsRenderModeType("GPURenderMode"), typeof(SKGLViewHandler));
                })
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                    fonts.AddFont("SegoeUI-Semibold.ttf", "SegoeSemibold");
                    fonts.AddFont("FluentSystemIcons-Regular.ttf", FluentUI.FontFamily);
                });
#if ANDROID
            Debug.WriteLine("[ANDROID-STARTUP] MauiProgram builder configured");
#endif

#if DEBUG
            builder.Logging.AddDebug();
            builder.Services.AddLogging(configure => configure.AddDebug());
#endif

            builder.Services.AddBluetoothServices();
            builder.Services.AddSingleton<SensorPacketDecoder>();
            builder.Services.AddSingleton<ISensorLogService, SensorLogService>();
            builder.Services.AddSingleton<IBluetoothSensorService, BluetoothSensorService>();
            builder.Services.AddSingleton<MainPageModel>();

#if ANDROID
            Debug.WriteLine("[ANDROID-STARTUP] MauiProgram builder.Build starting");
#endif
            var app = builder.Build();
#if ANDROID
            Debug.WriteLine("[ANDROID-STARTUP] MauiProgram builder.Build completed");
#endif
#if DEBUG
            VerifyLiveChartsRenderModeHandler(app);
#endif
            StartupDiagnostics.Log("[CHART] LiveChartsCore MAUI registration complete");
            return app;
        }

#if DEBUG
#if WINDOWS
        private static void AttachWindowsFirstChanceDiagnostics()
        {
            if (s_firstChanceDiagnosticsAttached)
            {
                return;
            }

            s_firstChanceDiagnosticsAttached = true;
            AppDomain.CurrentDomain.FirstChanceException += OnFirstChanceException;
        }

        private static void OnFirstChanceException(object? sender, FirstChanceExceptionEventArgs args)
        {
            if (args.Exception is PlatformNotSupportedException)
            {
                Debug.WriteLine($"[FIRST-CHANCE] PlatformNotSupportedException: {args.Exception}");
            }
        }
#endif

        private static void VerifyLiveChartsRenderModeHandler(MauiApp app)
        {
            var handlers = app.Services.GetRequiredService<IMauiHandlersFactory>();
            var handler = handlers.GetHandler(GetLiveChartsRenderModeType("CPURenderMode"));
            if (handler is null)
            {
                throw new InvalidOperationException("CPURenderMode handler registration resolved to null.");
            }

            StartupDiagnostics.Log("[CHART-HANDLER] CPURenderMode registration verified");
            StartupDiagnostics.Log($"[CHART-HANDLER] Handler type={handler.GetType().FullName}");
        }
#endif

        private static Type GetLiveChartsRenderModeType(string typeName)
        {
            var fullName = $"LiveChartsCore.SkiaSharpView.Maui.Rendering.{typeName}";
            return typeof(CartesianChart).Assembly.GetType(fullName, throwOnError: true)
                ?? throw new InvalidOperationException($"Unable to resolve {fullName}.");
        }
    }
}
