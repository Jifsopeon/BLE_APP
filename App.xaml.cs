using Microsoft.Extensions.DependencyInjection;

namespace BLE_APP
{
    public partial class App : Application
    {
        public App()
        {
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
    }
}
