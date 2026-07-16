namespace BLE_APP.Services;

public sealed record SensorLogTarget(string FileName, string Location, Stream Stream);

public static partial class SensorLogStorage
{
    public const string PublicFolderSelectionUnavailableMessage = "Public log folder selection is not available on this platform.";

    public static partial string? SelectedFolderDisplayName { get; }

    public static partial bool HasSelectedFolder { get; }

    public static partial Task<SensorLogTarget> OpenNewLogAsync(CancellationToken cancellationToken);

    public static partial Task<bool> SelectPublicFolderAsync(CancellationToken cancellationToken);
}
