#if WINDOWS
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace BLE_APP.Services;

public static partial class SensorLogStorage
{
    private static StorageFolder? s_sessionFolder;
    private static string? s_sessionFolderDisplayName;

    public static partial string? SelectedFolderDisplayName
        => s_sessionFolder is null ? null : s_sessionFolderDisplayName;

    public static partial bool HasSelectedFolder
        => s_sessionFolder is not null;

    public static partial async Task<SensorLogTarget> OpenNewLogAsync(CancellationToken cancellationToken)
    {
        var folder = await GetSelectedFolderAsync(cancellationToken).ConfigureAwait(false);
        var existing = await ListExistingLogNamesAsync(folder, cancellationToken).ConfigureAwait(false);

        var fileName = SensorLogFileNameAllocator.AllocateNextFileName(existing);
        StorageFile? logFile = null;
        var attempts = 0;
        while (logFile is null && attempts++ < 100)
        {
            try
            {
                logFile = await folder.CreateFileAsync(fileName, CreationCollisionOption.FailIfExists)
                    .AsTask(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception) when (existing.Add(fileName))
            {
                fileName = SensorLogFileNameAllocator.AllocateNextFileName(existing);
            }
        }

        if (logFile is null)
        {
            throw new IOException("Unable to allocate a new CSV log file in the selected folder.");
        }

        var stream = await logFile.OpenStreamForWriteAsync().ConfigureAwait(false);
        return new SensorLogTarget(fileName, logFile.Path, stream);
    }

    public static partial async Task<bool> SelectPublicFolderAsync(CancellationToken cancellationToken)
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };
        picker.FileTypeFilter.Add("*");

        var window = Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
        if (window is null)
        {
            throw new InvalidOperationException("No active Windows app window is available for folder selection.");
        }

        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));
        var folder = await picker.PickSingleFolderAsync().AsTask(cancellationToken).ConfigureAwait(false);
        if (folder is null)
        {
            return false;
        }

        s_sessionFolder = folder;
        s_sessionFolderDisplayName = folder.Path;
        return true;
    }

    private static Task<StorageFolder> GetSelectedFolderAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        if (s_sessionFolder is null)
        {
            throw new InvalidOperationException("Logging disabled: select a valid log folder.");
        }

        return Task.FromResult(s_sessionFolder);
    }

    private static async Task<HashSet<string>> ListExistingLogNamesAsync(StorageFolder folder, CancellationToken cancellationToken)
    {
        var files = await folder.GetFilesAsync().AsTask(cancellationToken).ConfigureAwait(false);
        return files
            .Select(file => file.Name)
            .Where(name => SensorLogFileNameAllocator.ParseLogNumber(name) > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
#endif
