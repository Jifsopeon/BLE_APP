using BLE_APP.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BLE_APP
{
    public partial class App : Application
    {
        private readonly IBluetoothSensorService _bluetooth;

        public App(IBluetoothSensorService bluetooth)
        {
            _bluetooth = bluetooth;
#if ANDROID
            System.Diagnostics.Debug.WriteLine("[ANDROID-STARTUP] App constructor entered");
#endif
            InitializeComponent();
#if ANDROID
            System.Diagnostics.Debug.WriteLine("[ANDROID] App launched");
            System.Diagnostics.Debug.WriteLine("[ANDROID-STARTUP] App constructor completed");
#endif
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            return new Window(new AppShell());
        }

        protected override void OnSleep()
        {
            base.OnSleep();
            _ = Task.Run(async () =>
            {
                try
                {
                    await _bluetooth.SuspendAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[BLE-LIFECYCLE] Suspend cleanup failed: {ex}");
                }
            });
        }

        protected override void OnResume()
        {
            base.OnResume();
            _ = Task.Run(async () =>
            {
                try
                {
                    await _bluetooth.ResumeAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[BLE-LIFECYCLE] Resume reconnect failed: {ex}");
                }
            });
        }
    }
}
