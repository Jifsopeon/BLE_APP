#if ANDROID
using Android.Content;
using Android.OS;
using BLE_APP.Models;
using Microsoft.Maui.ApplicationModel;
using Application = Android.App.Application;

namespace BLE_APP.Services;

internal static class AndroidBleForegroundServiceController
{
    public static bool IsRunning => AndroidBleForegroundService.IsRunning;

    public static async Task StartAsync(BluetoothConnectionState initialState, Action<string> log, CancellationToken cancellationToken)
    {
        if (!await AndroidNotificationPermissionGate.EnsureNotificationPermissionAsync(log, cancellationToken).ConfigureAwait(false))
        {
            log("[ANDROID-FGS] Notification permission is not granted; starting foreground service with limited notification visibility.");
        }

        var context = Platform.AppContext ?? Application.Context;
        var intent = new Intent(context, typeof(AndroidBleForegroundService));
        intent.SetAction(AndroidBleForegroundService.ActionStart);
        AndroidBleForegroundService.UpdateState(initialState);

        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            context.StartForegroundService(intent);
        }
        else
        {
            context.StartService(intent);
        }

        AndroidBleForegroundService.UpdateState(initialState);
        log("[ANDROID-FGS] Foreground BLE service start requested.");
    }

    public static Task StopAsync(Action<string> log)
    {
        try
        {
            var context = Platform.AppContext ?? Application.Context;
            AndroidBleForegroundService.RequestStop(context);

            var intent = new Intent(context, typeof(AndroidBleForegroundService));
            intent.SetAction(AndroidBleForegroundService.ActionStop);
            context.StopService(intent);
            log("[ANDROID-FGS] Foreground BLE service stop requested.");
        }
        catch (Exception ex)
        {
            log($"[ANDROID-FGS] Foreground BLE service stop failed: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    public static void UpdateState(BluetoothConnectionState state)
        => AndroidBleForegroundService.UpdateState(state);
}
#endif
