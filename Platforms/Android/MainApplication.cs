using Android.App;
using Android.Runtime;
using System.Diagnostics;

namespace BLE_APP
{
    [Application]
    public class MainApplication : MauiApplication
    {
        public MainApplication(IntPtr handle, JniHandleOwnership ownership)
            : base(handle, ownership)
        {
        }

        protected override MauiApp CreateMauiApp()
        {
            Debug.WriteLine("[ANDROID-STARTUP] MainApplication.CreateMauiApp entered");
            return MauiProgram.CreateMauiApp();
        }
    }
}
