namespace YASN.Sync;

public interface ISyncAppBridge
{
    Task<SyncManager.ConflictResolutionAction> ResolveConflictAsync(string fileName, DateTime localTime, DateTime remoteTime);

    Task ReloadNotesAsync();

    Task RefreshPreviewStylesAsync();
}
