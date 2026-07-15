using Android.App;
using Android.Content.PM;
using Android.OS;

namespace BLE_APP
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            System.Diagnostics.Debug.WriteLine("[ANDROID-STARTUP] MainActivity.OnCreate entered");
            base.OnCreate(savedInstanceState);
            System.Diagnostics.Debug.WriteLine("[ANDROID-STARTUP] MainActivity.OnCreate completed");
        }
    }
}
