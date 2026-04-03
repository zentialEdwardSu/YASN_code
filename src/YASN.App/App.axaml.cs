using System;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using YASN.Logging;
using YASN.Sync;

namespace YASN;

public partial class App : Application
{
    private const string MutexName = "Global\\YASN_SingleInstance";
    private static Mutex? _singleInstanceMutex;

    public static AppServices? Services { get; private set; }

    public MainWindow? MainWindow { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        if (!EnsureSingleInstance())
        {
            Dispatcher.UIThread.Post(() => desktop.Shutdown());
            base.OnFrameworkInitializationCompleted();
            return;
        }

        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        PreviewStyleManager.EnsureInitialized();
        AppLogger.Info("YASN started");

        MainWindow? mainWindow = null;
        var windowManager = new NoteWindowManager(
            NoteManager.Instance,
            () => mainWindow,
            GetActiveWindow);
        var syncManager = new SyncManager(windowManager);
        Services = new AppServices(NoteManager.Instance, windowManager, syncManager);
        windowManager.AttachServices(Services);
        mainWindow = new MainWindow(Services);
        MainWindow = mainWindow;

        desktop.MainWindow = MainWindow;
        MainWindow.Hide();

        desktop.Exit += OnDesktopExit;

        windowManager.RestoreOpenNotes();
        if (!desktop.Windows.OfType<FloatingNoteWindow>().Any(static window => window.IsVisible))
        {
            MainWindow.Show();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static bool EnsureSingleInstance()
    {
        try
        {
            _singleInstanceMutex = new Mutex(true, MutexName, out var createdNew);
            return createdNew;
        }
        catch
        {
            return true;
        }
    }

    private static void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        if (Services != null)
        {
            Services.NoteWindowManager.PrepareForShutdown();
            Services.SyncManager.Dispose();
        }

        try
        {
            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();
            _singleInstanceMutex = null;
        }
        catch
        {
        }
    }

    private static Window? GetActiveWindow()
    {
        if (Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return null;
        }

        foreach (Window window in desktop.Windows)
        {
            if (window.IsActive)
            {
                return window;
            }
        }

        return desktop.MainWindow;
    }

    private void CreateNormalNote_OnClick(object? sender, EventArgs e)
    {
        Services?.NoteWindowManager.CreateAndOpenNote(WindowLevel.Normal);
    }

    private void CreateTopMostNote_OnClick(object? sender, EventArgs e)
    {
        Services?.NoteWindowManager.CreateAndOpenNote(WindowLevel.TopMost);
    }

    private void CreateBottomMostNote_OnClick(object? sender, EventArgs e)
    {
        Services?.NoteWindowManager.CreateAndOpenNote(WindowLevel.BottomMost);
    }

    private void ShowMainWindow_OnClick(object? sender, EventArgs e)
    {
        Services?.NoteWindowManager.ShowMainWindow();
    }

    private async void OpenSettings_OnClick(object? sender, EventArgs e)
    {
        if (Services == null)
        {
            return;
        }

        await Services.NoteWindowManager.OpenSettingsAsync();
    }

    private void ExitApplication_OnClick(object? sender, EventArgs e)
    {
        Services?.NoteWindowManager.PrepareForShutdown();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }
}
