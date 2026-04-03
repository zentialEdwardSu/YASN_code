using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace YASN;

public partial class MainWindow : Window
{
    private readonly AppServices _services;

    public MainWindow(AppServices services)
    {
        InitializeComponent();
        _services = services;
        NotesItemsControl.ItemsSource = _services.NoteManager.Notes;
        _services.NoteManager.Notes.CollectionChanged += Notes_CollectionChanged;
        Activated += (_, _) => UpdateDashboardState();
        Closing += OnClosing;
        UpdateDashboardState();
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }

    private void CreateNormalNote_OnClick(object? sender, RoutedEventArgs e)
    {
        _services.NoteWindowManager.CreateAndOpenNote(WindowLevel.Normal);
        UpdateDashboardState();
    }

    private void CreateTopMostNote_OnClick(object? sender, RoutedEventArgs e)
    {
        _services.NoteWindowManager.CreateAndOpenNote(WindowLevel.TopMost);
        UpdateDashboardState();
    }

    private void CreateBottomMostNote_OnClick(object? sender, RoutedEventArgs e)
    {
        _services.NoteWindowManager.CreateAndOpenNote(WindowLevel.BottomMost);
        UpdateDashboardState();
    }

    private void RefreshList_OnClick(object? sender, RoutedEventArgs e)
    {
        NotesItemsControl.ItemsSource = null;
        NotesItemsControl.ItemsSource = _services.NoteManager.Notes;
        UpdateDashboardState();
    }

    private async void OpenSettings_OnClick(object? sender, RoutedEventArgs e)
    {
        await _services.NoteWindowManager.OpenSettingsAsync();
    }

    private async void DeleteNote_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: NoteData note })
        {
            return;
        }

        var shouldDelete = await DialogService.ShowConfirmationAsync(
            this,
            "删除便签",
            $"确定要删除“{note.Title}”吗？");
        if (!shouldDelete)
        {
            return;
        }

        _services.NoteWindowManager.CloseNote(note);
        _services.NoteManager.DeleteNote(note);
        UpdateDashboardState();
    }

    private void OpenNote_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: NoteData note })
        {
            _services.NoteWindowManager.OpenNote(note);
            UpdateDashboardState();
        }
    }

    private void CloseNote_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: NoteData note })
        {
            _services.NoteWindowManager.CloseNote(note);
            UpdateDashboardState();
        }
    }

    private void SetNormalLevel_OnClick(object? sender, RoutedEventArgs e)
    {
        SetLevel(sender, WindowLevel.Normal);
    }

    private void SetTopMostLevel_OnClick(object? sender, RoutedEventArgs e)
    {
        SetLevel(sender, WindowLevel.TopMost);
    }

    private void SetBottomMostLevel_OnClick(object? sender, RoutedEventArgs e)
    {
        SetLevel(sender, WindowLevel.BottomMost);
    }

    private void SetLevel(object? sender, WindowLevel level)
    {
        if (sender is not Control { Tag: NoteData note })
        {
            return;
        }

        _services.NoteWindowManager.SetWindowLevel(note, level);
        UpdateDashboardState();
    }

    private void TitleEditor_OnLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is not TextBox { Tag: NoteData note } textBox)
        {
            return;
        }

        note.Title = textBox.Text?.Trim() ?? string.Empty;
        _services.NoteManager.UpdateNote(note);
        UpdateDashboardState();
    }

    private void OpenDataFolder_OnClick(object? sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = AppPaths.DataDirectory,
            UseShellExecute = true
        });
    }

    private void HideToTray_OnClick(object? sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void Notes_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateDashboardState();
    }

    private void UpdateDashboardState()
    {
        var totalCount = _services.NoteManager.Notes.Count;
        var openCount = _services.NoteManager.Notes.Count(note => note.IsOpen);

        TotalNotesTextBlock.Text = totalCount.ToString(CultureInfo.InvariantCulture);
        OpenNotesTextBlock.Text = openCount.ToString(CultureInfo.InvariantCulture);
        NoNotesPanel.IsVisible = totalCount == 0;
    }
}
