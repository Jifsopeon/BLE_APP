using System.Diagnostics;
using Microsoft.UI.Xaml;

namespace BLE_APP.WinUI
{
    public partial class App : MauiWinUIApplication
    {
        public App()
        {
            InitializeComponent();
#if DEBUG
            UnhandledException += OnUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
            Debug.WriteLine("[WINUI-EXCEPTION-HANDLER] Registered");
#endif
        }

        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

#if DEBUG
        private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            Debug.WriteLine("[WINUI-EXCEPTION-HANDLER] Invoked");
            Debug.WriteLine("========== WINDOWS UNHANDLED EXCEPTION ==========");
            Debug.WriteLine($"UI thread: {Microsoft.Maui.ApplicationModel.MainThread.IsMainThread}");
            Debug.WriteLine($"ManagedThreadId: {Environment.CurrentManagedThreadId}");
            Debug.WriteLine($"Dispatcher HasThreadAccess: {(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()?.HasThreadAccess.ToString() ?? "<no dispatcher>")}");
            Debug.WriteLine($"WinUI Message: {e.Message}");
            LogException(e.Exception, e.Message);
            Debug.WriteLine("=================================================");
            e.Handled = false;
        }

        private static void OnDomainUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
        {
            Debug.WriteLine("[APPDOMAIN-UNHANDLED]");
            Debug.WriteLine("========== APPDOMAIN UNHANDLED EXCEPTION ==========");
            Debug.WriteLine($"IsTerminating: {e.IsTerminating}");
            LogException(e.ExceptionObject as Exception, e.ExceptionObject?.ToString() ?? "<null>");
            Debug.WriteLine("===================================================");
        }

        private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            Debug.WriteLine("[TASK-UNOBSERVED]");
            Debug.WriteLine("========== TASK UNOBSERVED EXCEPTION ==========");
            LogException(e.Exception, e.Exception.Message);
            Debug.WriteLine("===============================================");
        }

        private static void LogException(Exception? exception, string fallbackMessage)
        {
            Debug.WriteLine($"Type: {exception?.GetType().FullName ?? "<unknown>"}");
            Debug.WriteLine($"Message: {exception?.Message ?? fallbackMessage}");
            Debug.WriteLine(exception?.ToString() ?? fallbackMessage);

            var inner = exception?.InnerException;
            var depth = 0;
            while (inner is not null)
            {
                Debug.WriteLine($"InnerException[{depth}]: {inner.GetType().FullName}");
                Debug.WriteLine($"InnerMessage[{depth}]: {inner.Message}");
                Debug.WriteLine(inner.ToString());
                inner = inner.InnerException;
                depth++;
            }
        }
#endif
    }
}
