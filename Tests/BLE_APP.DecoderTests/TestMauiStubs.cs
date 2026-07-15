namespace Microsoft.Maui.ApplicationModel;

internal static class MainThread
{
    public static bool IsMainThread => true;

    public static void BeginInvokeOnMainThread(Action action)
        => action();
}
