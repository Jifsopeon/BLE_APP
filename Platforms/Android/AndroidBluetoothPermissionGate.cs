using Android;
using Android.App;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using Microsoft.Maui.ApplicationModel;

namespace BLE_APP.Services;

internal static class AndroidBluetoothPermissionGate
{
    private static readonly SemaphoreSlim PermissionGate = new(1, 1);

    public static async Task<bool> EnsureBluetoothPermissionsAsync(Action<string> log, CancellationToken cancellationToken)
    {
        await PermissionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var activity = Platform.CurrentActivity;
            LogActivity(log, activity);
            LogRawPermissionState(log, "Pre-request");

            if (HasRequiredPermissions())
            {
                log("[ANDROID-PERMISSION] Request result=AlreadyGranted");
                return true;
            }

            if (activity is null)
            {
                log("[ANDROID-PERMISSION] Request result=NoActiveActivity");
                return false;
            }

            log("[ANDROID-PERMISSION] Request started");
            var status = await MainThread.InvokeOnMainThreadAsync(
                    () => Permissions.RequestAsync<BluetoothScanConnectPermission>())
                .ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            log($"[ANDROID-PERMISSION] Request result={status}");
            LogRawPermissionState(log, "Post-request");

            if (HasRequiredPermissions())
            {
                return true;
            }

            if (IsPermanentDenial(activity))
            {
                log("Nearby devices permission is required to scan. Enable it in Android Settings for BLE_APP.");
            }

            return false;
        }
        finally
        {
            PermissionGate.Release();
        }
    }

    private static bool HasRequiredPermissions()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            return IsGranted(Manifest.Permission.BluetoothScan)
                && IsGranted(Manifest.Permission.BluetoothConnect);
        }

        return IsGranted(Manifest.Permission.AccessFineLocation);
    }

    private static void LogRawPermissionState(Action<string> log, string prefix)
    {
        log($"[ANDROID-PERMISSION] SDK={(int)Build.VERSION.SdkInt}");

        if (OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            log($"[ANDROID-PERMISSION] {prefix} BLUETOOTH_SCAN={FormatPermission(Manifest.Permission.BluetoothScan)}");
            log($"[ANDROID-PERMISSION] {prefix} BLUETOOTH_CONNECT={FormatPermission(Manifest.Permission.BluetoothConnect)}");
            log("[ANDROID-PERMISSION] ACCESS_FINE_LOCATION=NotApplicable");
            return;
        }

        log("[ANDROID-PERMISSION] BLUETOOTH_SCAN=NotApplicable");
        log("[ANDROID-PERMISSION] BLUETOOTH_CONNECT=NotApplicable");
        log($"[ANDROID-PERMISSION] {prefix} ACCESS_FINE_LOCATION={FormatPermission(Manifest.Permission.AccessFineLocation)}");
    }

    private static void LogActivity(Action<string> log, Activity? activity)
    {
        log($"[ANDROID-PERMISSION] Activity null={activity is null}");
        log($"[ANDROID-PERMISSION] Activity type={activity?.GetType().FullName ?? "<none>"}");
        log($"[ANDROID-PERMISSION] Activity hasWindowFocus={activity?.HasWindowFocus ?? false}");
    }

    private static bool IsPermanentDenial(Activity activity)
    {
        var permissions = RequiredPermissionNames();
        return permissions.Any(permission =>
            !IsGranted(permission)
            && !ActivityCompat.ShouldShowRequestPermissionRationale(activity, permission));
    }

    private static string[] RequiredPermissionNames()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            return
            [
                Manifest.Permission.BluetoothScan,
                Manifest.Permission.BluetoothConnect
            ];
        }

        return [Manifest.Permission.AccessFineLocation];
    }

    private static string FormatPermission(string permission)
        => IsGranted(permission) ? "Granted" : "Denied";

    private static bool IsGranted(string permission)
        => ContextCompat.CheckSelfPermission(Platform.AppContext, permission) == Permission.Granted;

    private sealed class BluetoothScanConnectPermission : Permissions.BasePlatformPermission
    {
        public override (string androidPermission, bool isRuntime)[] RequiredPermissions
            => OperatingSystem.IsAndroidVersionAtLeast(31)
                ?
                [
                    (Manifest.Permission.BluetoothScan, true),
                    (Manifest.Permission.BluetoothConnect, true)
                ]
                : [(Manifest.Permission.AccessFineLocation, true)];
    }
}
