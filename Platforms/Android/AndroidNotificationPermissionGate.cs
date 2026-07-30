#if ANDROID
using Android;
using Android.App;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using Microsoft.Maui.ApplicationModel;

namespace BLE_APP.Services;

internal static class AndroidNotificationPermissionGate
{
    private static readonly SemaphoreSlim PermissionGate = new(1, 1);

    public static async Task<bool> EnsureNotificationPermissionAsync(Action<string> log, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            return true;
        }

        await PermissionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsGranted())
            {
                return true;
            }

            var activity = Platform.CurrentActivity;
            if (activity is null)
            {
                log("[ANDROID-NOTIFICATION] Permission request skipped because no active activity is available.");
                return false;
            }

            var status = await MainThread.InvokeOnMainThreadAsync(
                    () => Permissions.RequestAsync<PostNotificationsPermission>())
                .ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            log($"[ANDROID-NOTIFICATION] Permission request result={status}");

            if (!IsGranted() && !ActivityCompat.ShouldShowRequestPermissionRationale(activity, Manifest.Permission.PostNotifications))
            {
                log("Notification permission is required to show BLE background monitoring status. Enable notifications in Android Settings for BLE_APP.");
            }

            return IsGranted();
        }
        finally
        {
            PermissionGate.Release();
        }
    }

    private static bool IsGranted()
        => ContextCompat.CheckSelfPermission(Platform.AppContext, Manifest.Permission.PostNotifications) == Permission.Granted;

    private sealed class PostNotificationsPermission : Permissions.BasePlatformPermission
    {
        public override (string androidPermission, bool isRuntime)[] RequiredPermissions
            => [(Manifest.Permission.PostNotifications, true)];
    }
}
#endif
