#if ANDROID
using Android.App;
using Android.Content;
using Android.OS;
using BLE_APP.Models;

namespace BLE_APP.Services;

[Service(
    Name = "com.companyname.bleapp.AndroidBleForegroundService",
    Exported = false,
    ForegroundServiceType = Android.Content.PM.ForegroundService.TypeConnectedDevice)]
public sealed class AndroidBleForegroundService : Service
{
    internal const string ActionStart = "BLE_APP.action.START_BLE_FOREGROUND";
    internal const string ActionStop = "BLE_APP.action.STOP_BLE_FOREGROUND";
    internal const int NotificationId = 8401;
    internal const string ChannelId = "pse84_iaq_ble";

    private static readonly object Gate = new();
    private static AndroidBleForegroundService? s_instance;
    private static BluetoothConnectionState s_lastState = BluetoothConnectionState.Connecting;
    private static bool s_stopRequested;

    public static bool IsRunning { get; private set; }

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        if (intent?.Action == ActionStop)
        {
            System.Diagnostics.Debug.WriteLine("[ANDROID-FGS] Stop action received.");
            s_stopRequested = true;
            StopForegroundServiceInstance("ActionStop");
            return StartCommandResult.NotSticky;
        }

        if (intent is null && s_stopRequested)
        {
            System.Diagnostics.Debug.WriteLine("[ANDROID-FGS] Restart suppressed after stop request.");
            StopForegroundServiceInstance("RestartSuppressed");
            return StartCommandResult.NotSticky;
        }

        lock (Gate)
        {
            s_stopRequested = false;
            s_instance = this;
            IsRunning = true;
        }

        CreateNotificationChannel();
        StartForeground(NotificationId, BuildNotification(s_lastState));
        System.Diagnostics.Debug.WriteLine("[ANDROID-FGS] StartForeground executed.");
        return StartCommandResult.NotSticky;
    }

    public override void OnDestroy()
    {
        System.Diagnostics.Debug.WriteLine("[ANDROID-FGS] OnDestroy called.");
        RemoveForegroundNotification("OnDestroy");

        lock (Gate)
        {
            if (ReferenceEquals(s_instance, this))
            {
                s_instance = null;
            }

            IsRunning = false;
        }

        base.OnDestroy();
    }

    public override void OnTaskRemoved(Intent? rootIntent)
    {
        System.Diagnostics.Debug.WriteLine($"[ANDROID-FGS] OnTaskRemoved called. State={s_lastState}; IsRunning={IsRunning}.");
        if (s_lastState is BluetoothConnectionState.Disconnected or BluetoothConnectionState.Error or BluetoothConnectionState.Idle)
        {
            s_stopRequested = true;
            StopForegroundServiceInstance("TaskRemovedFinalState");
        }

        base.OnTaskRemoved(rootIntent);
    }

    internal static void UpdateState(BluetoothConnectionState state)
    {
        AndroidBleForegroundService? instance;
        lock (Gate)
        {
            s_lastState = state;
            instance = s_instance;
        }

        if (instance is null)
        {
            return;
        }

        if (state is BluetoothConnectionState.Disconnected or BluetoothConnectionState.Error or BluetoothConnectionState.Idle)
        {
            System.Diagnostics.Debug.WriteLine($"[ANDROID-FGS] Final state notification update suppressed: {state}.");
            return;
        }

        try
        {
            var manager = (NotificationManager?)instance.GetSystemService(NotificationService);
            manager?.Notify(NotificationId, instance.BuildNotification(state));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ANDROID-FGS] Notification update failed: {ex}");
        }
    }

    internal static void RequestStop(Context context)
    {
        AndroidBleForegroundService? instance;
        lock (Gate)
        {
            s_stopRequested = true;
            instance = s_instance;
        }

        if (instance is not null)
        {
            instance.StopForegroundServiceInstance("Controller");
            return;
        }

        try
        {
            var manager = (NotificationManager?)context.GetSystemService(NotificationService);
            manager?.Cancel(NotificationId);
            System.Diagnostics.Debug.WriteLine("[ANDROID-FGS] Notification cancelled without active service instance.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ANDROID-FGS] Notification cancel failed without active service instance: {ex}");
        }
    }

    private void StopForegroundServiceInstance(string reason)
    {
        System.Diagnostics.Debug.WriteLine($"[ANDROID-FGS] Foreground-service stop requested. Reason={reason}.");
        try
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.N)
            {
                StopForeground(StopForegroundFlags.Remove);
            }
            else
            {
#pragma warning disable CA1422
                StopForeground(true);
#pragma warning restore CA1422
            }

            System.Diagnostics.Debug.WriteLine("[ANDROID-FGS] StopForeground executed.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ANDROID-FGS] StopForeground failed: {ex}");
        }

        RemoveForegroundNotification(reason);
        StopSelf();
        System.Diagnostics.Debug.WriteLine("[ANDROID-FGS] StopSelf executed.");
    }

    private void RemoveForegroundNotification(string reason)
    {
        try
        {
            var manager = (NotificationManager?)GetSystemService(NotificationService);
            manager?.Cancel(NotificationId);
            System.Diagnostics.Debug.WriteLine($"[ANDROID-FGS] Notification cancelled. Reason={reason}.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ANDROID-FGS] Notification cancel failed. Reason={reason}; Error={ex}");
        }
    }

    private void CreateNotificationChannel()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O)
        {
            return;
        }

        var manager = (NotificationManager?)GetSystemService(NotificationService);
        var channel = new NotificationChannel(ChannelId, "PSE84 IAQ sensor", NotificationImportance.Low)
        {
            Description = "Shows active BLE sensor monitoring status."
        };
        manager?.CreateNotificationChannel(channel);
    }

    private Notification BuildNotification(BluetoothConnectionState state)
    {
        var launchIntent = PackageManager?.GetLaunchIntentForPackage(PackageName ?? string.Empty)
            ?? new Intent(this, typeof(MainActivity));
        launchIntent.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);

        var pendingFlags = PendingIntentFlags.UpdateCurrent;
        if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
        {
            pendingFlags |= PendingIntentFlags.Immutable;
        }

        var contentIntent = PendingIntent.GetActivity(this, 0, launchIntent, pendingFlags);
        var builder = Build.VERSION.SdkInt >= BuildVersionCodes.O
            ? new Notification.Builder(this, ChannelId)
            : new Notification.Builder(this);

        return builder
            .SetContentTitle("PSE84 IAQ")
            .SetContentText(GetNotificationText(state))
            .SetSmallIcon(Resource.Mipmap.pse84_iaq_appicon)
            .SetContentIntent(contentIntent)
            .SetOngoing(true)
            .SetOnlyAlertOnce(true)
            .SetShowWhen(false)
            .SetCategory(Notification.CategoryService)
            .Build();
    }

    private static string GetNotificationText(BluetoothConnectionState state)
        => state switch
        {
            BluetoothConnectionState.Connecting or BluetoothConnectionState.DiscoveringServices or BluetoothConnectionState.Subscribing => "Connecting to sensor",
            BluetoothConnectionState.Connected => "Sensor connected",
            BluetoothConnectionState.ReceivingData => "Receiving sensor data",
            BluetoothConnectionState.Reconnecting => "Reconnecting",
            BluetoothConnectionState.Disconnected or BluetoothConnectionState.Error => "Disconnected",
            _ => state.ToString()
        };
}
#endif
