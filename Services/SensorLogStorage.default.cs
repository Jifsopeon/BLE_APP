#if !WINDOWS && !ANDROID
namespace BLE_APP.Services;

public static partial class SensorLogStorage
{
    public static partial string? SelectedFolderDisplayName => null;

    public static partial bool HasSelectedFolder => false;

    public static partial Task<SensorLogTarget> OpenNewLogAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        throw new PlatformNotSupportedException(PublicFolderSelectionUnavailableMessage);
    }

    public static partial Task<bool> SelectPublicFolderAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        throw new PlatformNotSupportedException(PublicFolderSelectionUnavailableMessage);
    }

}
#endif
