using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Threading;
using YASN.Sync;

namespace YASN;

public sealed class NoteWindowManager : ISyncAppBridge
{
    private readonly Func<Window?> _activeWindowAccessor;
    private readonly Func<MainWindow?> _mainWindowAccessor;
    private readonly NoteManager _noteManager;
    private readonly Dictionary<int, FloatingNoteWindow> _windows = new();
    private AppServices? _services;
    private bool _isReloading;

    public NoteWindowManager(
        NoteManager noteManager,
        Func<MainWindow?> mainWindowAccessor,
        Func<Window?> activeWindowAccessor)
    {
        _noteManager = noteManager;
        _mainWindowAccessor = mainWindowAccessor;
        _activeWindowAccessor = activeWindowAccessor;
    }

    public bool IsApplicationShuttingDown { get; private set; }

    public void AttachServices(AppServices services)
    {
        _services = services;
    }

    public NoteData CreateAndOpenNote(WindowLevel level)
    {
        var note = _noteManager.CreateNote(level);
        OpenNote(note);
        return note;
    }

    public void OpenNote(NoteData note)
    {
        if (_windows.TryGetValue(note.Id, out var existingWindow))
        {
            existingWindow.Show();
            existingWindow.Activate();
            return;
        }

        var window = new FloatingNoteWindow(note, this);
        _windows[note.Id] = window;
        note.IsOpen = true;
        _noteManager.UpdateNote(note);
        window.Show();
        window.Activate();
    }

    public void CloseNote(NoteData note)
    {
        if (_windows.TryGetValue(note.Id, out var window))
        {
            window.Close();
            return;
        }

        note.IsOpen = false;
        _noteManager.UpdateNote(note);
    }

    public void SetWindowLevel(NoteData note, WindowLevel level)
    {
        note.Level = level;
        _noteManager.UpdateNote(note);

        if (_windows.TryGetValue(note.Id, out var window))
        {
            window.ApplyWindowLevel(level);
            window.RefreshTaskbarVisibilityFromSettings();
        }
    }

    public bool IsWindowOpen(NoteData note)
    {
        return _windows.ContainsKey(note.Id);
    }

    public void OnNoteWindowClosed(NoteData note)
    {
        _windows.Remove(note.Id);

        if (!IsApplicationShuttingDown && !_isReloading)
        {
            note.IsOpen = false;
        }

        _noteManager.UpdateNote(note);
    }

    public void RestoreOpenNotes()
    {
        foreach (var note in _noteManager.Notes.Where(static n => n.IsOpen))
        {
            OpenNote(note);
        }
    }

    public void RefreshTaskbarVisibility()
    {
        foreach (var window in _windows.Values)
        {
            window.RefreshTaskbarVisibilityFromSettings();
        }
    }

    public void RefreshChromeBehavior()
    {
        foreach (var window in _windows.Values)
        {
            window.RefreshChromeBehaviorFromSettings();
        }
    }

    public void RefreshPreviewStyles()
    {
        foreach (var window in _windows.Values)
        {
            window.RefreshPreviewStyleFromSettings();
        }
    }

    public void PrepareForShutdown()
    {
        IsApplicationShuttingDown = true;
    }

    public void ShowMainWindow()
    {
        var window = _mainWindowAccessor();
        if (window == null)
        {
            return;
        }

        window.Show();
        window.WindowState = WindowState.Normal;
        window.Activate();
    }

    public async Task OpenSettingsAsync()
    {
        var owner = _activeWindowAccessor() ?? _mainWindowAccessor();
        var settingsWindow = new SettingsWindow(this, _services?.SyncManager);
        if (owner != null)
        {
            await settingsWindow.ShowDialog(owner);
            return;
        }

        settingsWindow.Show();
    }

    public async Task<SyncManager.ConflictResolutionAction> ResolveConflictAsync(string fileName, DateTime localTime, DateTime remoteTime)
    {
        var owner = _activeWindowAccessor() ?? _mainWindowAccessor();
        var message =
            $"远端文件 {fileName} 比本地更新，且内容不一致。\n本地修改时间：{localTime:G}\n远端修改时间：{remoteTime:G}\n确定将远端版本下载并覆盖本地吗？";
        var useRemote = await Dispatcher.UIThread.InvokeAsync(() =>
            DialogService.ShowConfirmationAsync(owner, "同步冲突", message));
        return useRemote
            ? SyncManager.ConflictResolutionAction.DownloadRemote
            : SyncManager.ConflictResolutionAction.KeepLocal;
    }

    public async Task ReloadNotesAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _isReloading = true;
            try
            {
                foreach (var window in _windows.Values.ToArray())
                {
                    window.Close();
                }

                _windows.Clear();
                _noteManager.ReloadNotes();
                RestoreOpenNotes();
            }
            finally
            {
                _isReloading = false;
            }
        });
    }

    public async Task RefreshPreviewStylesAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(RefreshPreviewStyles);
    }
}
