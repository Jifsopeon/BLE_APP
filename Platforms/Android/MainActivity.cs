using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using BLE_APP.Services;

namespace BLE_APP
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        internal const int LogFolderRequestCode = 4206;

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            System.Diagnostics.Debug.WriteLine("[ANDROID-STARTUP] MainActivity.OnCreate entered");
            base.OnCreate(savedInstanceState);
            System.Diagnostics.Debug.WriteLine("[ANDROID-STARTUP] MainActivity.OnCreate completed");
        }

        protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
        {
            base.OnActivityResult(requestCode, resultCode, data);
            if (requestCode == LogFolderRequestCode)
            {
                AndroidLogFolderPicker.Complete(this, resultCode, data);
            }
        }
    }
}
