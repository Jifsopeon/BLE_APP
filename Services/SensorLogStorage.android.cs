#if ANDROID
using Android.App;
using Android.Content;
using Android.Provider;
using Microsoft.Maui.ApplicationModel;
using Application = Android.App.Application;
using Uri = Android.Net.Uri;

namespace BLE_APP.Services;

public static partial class SensorLogStorage
{
    private static Uri? s_sessionTreeUri;
    private static string? s_sessionTreeDisplayName;

    public static partial string? SelectedFolderDisplayName
        => s_sessionTreeUri is null ? null : s_sessionTreeDisplayName;

    public static partial bool HasSelectedFolder
        => s_sessionTreeUri is not null;

    public static partial async Task<SensorLogTarget> OpenNewLogAsync(CancellationToken cancellationToken)
    {
        var treeUri = GetSessionTreeUri();
        if (treeUri is null)
        {
            throw new InvalidOperationException("Logging disabled: select a valid log folder.");
        }

        var destinationUri = AndroidDocumentTree.GetRootDocumentUri(treeUri);
        if (destinationUri is null)
        {
            throw new InvalidOperationException("Logging disabled: select a valid log folder.");
        }

        var existing = AndroidDocumentTree.ListChildFileNames(treeUri, destinationUri)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var fileName = SensorLogFileNameAllocator.AllocateNextFileName(existing);
        Uri? documentUri;
        var attempts = 0;
        do
        {
            documentUri = AndroidDocumentTree.CreateDocument(destinationUri, "text/csv", fileName);
            if (documentUri is null)
            {
                existing.Add(fileName);
                fileName = SensorLogFileNameAllocator.AllocateNextFileName(existing);
            }
        }
        while (documentUri is null && ++attempts < 100);

        if (documentUri is null)
        {
            throw new IOException("Unable to create a CSV log document in the selected Android folder.");
        }

        var stream = Application.Context.ContentResolver?.OpenOutputStream(documentUri, "wt");
        if (stream is null)
        {
            throw new IOException("Unable to open the selected Android log document for writing.");
        }

        await Task.CompletedTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new SensorLogTarget(fileName, documentUri.ToString() ?? string.Empty, stream);
    }

    public static partial async Task<bool> SelectPublicFolderAsync(CancellationToken cancellationToken)
    {
        var uri = await AndroidLogFolderPicker.PickFolderAsync(cancellationToken).ConfigureAwait(false);
        if (uri is null)
        {
            return false;
        }

        s_sessionTreeUri = uri;
        s_sessionTreeDisplayName = AndroidDocumentTree.GetDisplayName(uri) ?? "Selected Android folder";
        return true;
    }

    private static Uri? GetSessionTreeUri()
    {
        if (s_sessionTreeUri is null)
        {
            return null;
        }

        return s_sessionTreeUri;
    }
}

internal static class AndroidDocumentTree
{
    private static readonly string[] ChildProjection =
    [
        DocumentsContract.Document.ColumnDocumentId,
        DocumentsContract.Document.ColumnDisplayName,
        DocumentsContract.Document.ColumnMimeType
    ];

    public static Uri? GetRootDocumentUri(Uri treeUri)
    {
        var rootId = DocumentsContract.GetTreeDocumentId(treeUri);
        if (string.IsNullOrWhiteSpace(rootId) || Application.Context.ContentResolver is null)
        {
            return null;
        }

        return DocumentsContract.BuildDocumentUriUsingTree(treeUri, rootId);
    }

    public static IEnumerable<string> ListChildFileNames(Uri treeUri, Uri directoryUri)
    {
        var directoryId = DocumentsContract.GetDocumentId(directoryUri);
        var childrenUri = string.IsNullOrWhiteSpace(directoryId)
            ? null
            : DocumentsContract.BuildChildDocumentsUriUsingTree(treeUri, directoryId);
        if (childrenUri is null || Application.Context.ContentResolver is null)
        {
            yield break;
        }

        using var cursor = Application.Context.ContentResolver?.Query(
            childrenUri,
            ChildProjection,
            null,
            null,
            null);

        if (cursor is null)
        {
            yield break;
        }

        while (cursor.MoveToNext())
        {
            var name = cursor.GetString(1);
            var mimeType = cursor.GetString(2);
            if (!string.IsNullOrWhiteSpace(name) && mimeType != DocumentsContract.Document.MimeTypeDir)
            {
                yield return name;
            }
        }
    }

    public static Uri? CreateDocument(Uri parentUri, string mimeType, string displayName)
        => Application.Context.ContentResolver is null
            ? null
            : DocumentsContract.CreateDocument(Application.Context.ContentResolver, parentUri, mimeType, displayName);

    public static string? GetDisplayName(Uri treeUri)
    {
        var rootId = DocumentsContract.GetTreeDocumentId(treeUri);
        if (string.IsNullOrWhiteSpace(rootId) || Application.Context.ContentResolver is null)
        {
            return null;
        }

        var rootDocumentUri = DocumentsContract.BuildDocumentUriUsingTree(treeUri, rootId);
        if (rootDocumentUri is null)
        {
            return null;
        }

        using var cursor = Application.Context.ContentResolver.Query(
            rootDocumentUri,
            [DocumentsContract.Document.ColumnDisplayName],
            null,
            null,
            null);

        return cursor is not null && cursor.MoveToFirst()
            ? cursor.GetString(0)
            : null;
    }
}

internal static class AndroidLogFolderPicker
{
    private static TaskCompletionSource<Uri?>? _pendingPicker;

    public static Task<Uri?> PickFolderAsync(CancellationToken cancellationToken)
    {
        var activity = Platform.CurrentActivity;
        if (activity is null)
        {
            throw new InvalidOperationException("No active Android activity is available for folder selection.");
        }

        if (_pendingPicker is not null)
        {
            throw new InvalidOperationException("A log folder picker is already active.");
        }

        _pendingPicker = new TaskCompletionSource<Uri?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(() => _pendingPicker?.TrySetCanceled(cancellationToken));

        var intent = new Intent(Intent.ActionOpenDocumentTree);
        intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission | ActivityFlags.GrantPrefixUriPermission);
        activity.StartActivityForResult(intent, MainActivity.LogFolderRequestCode);
        return _pendingPicker.Task;
    }

    public static void Complete(Activity activity, Result resultCode, Intent? data)
    {
        var pending = _pendingPicker;
        _pendingPicker = null;

        if (pending is null)
        {
            return;
        }

        if (resultCode != Result.Ok || data?.Data is null)
        {
            pending.TrySetResult(null);
            return;
        }

        var uri = data.Data;
        pending.TrySetResult(uri);
    }
}
#endif
