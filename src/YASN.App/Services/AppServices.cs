using YASN.Sync;

namespace YASN;

public sealed class AppServices
{
    public AppServices(NoteManager noteManager, NoteWindowManager noteWindowManager, SyncManager syncManager)
    {
        NoteManager = noteManager;
        NoteWindowManager = noteWindowManager;
        SyncManager = syncManager;
    }

    public NoteManager NoteManager { get; }

    public NoteWindowManager NoteWindowManager { get; }

    public SyncManager SyncManager { get; }
}
