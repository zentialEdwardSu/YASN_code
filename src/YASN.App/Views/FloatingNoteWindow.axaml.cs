using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using IconPacks.Avalonia.Material;
using Markdown.Avalonia;
using YASN.Logging;
using YASN.Settings;

namespace YASN;

public partial class FloatingNoteWindow : Window
{
    private static readonly IntPtr HwndBottom = new(1);
    private static readonly IntPtr HwndNoTopMost = new(-2);
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private const double ExpandedHeaderHeight = 56;
    private const double ExpandedToolbarHeight = 42;

    private readonly DispatcherTimer _bottomMostTimer;
    private readonly DispatcherTimer _previewDebounceTimer;
    private readonly DispatcherTimer _chromeTimer;
    private readonly string _attachmentDirectory;
    private readonly string _backgroundImageDirectory;
    private readonly string _imageDirectory;
    private readonly string[] _headerColors =
    [
        "#E6D4C5E0",
        "#E6FFB6C1",
        "#E6B0E0E6",
        "#E6C8E6C9",
        "#E6FFE4B5",
        "#E6F5DEB3"
    ];
    private readonly double[] _backgroundOpacitySteps = [0.05, 0.15, 0.3, 0.5, 0.75, 1.0];
    private readonly MarkdownScrollViewer _previewViewer = new();
    private readonly NoteWindowManager _windowManager;
    private IntPtr _platformHandle;
    private bool _isPointerInside;
    private bool _autoCollapseChromeEnabled;
    private bool _chromeExpanded = true;
    private EditorDisplayMode _editorDisplayMode;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    public FloatingNoteWindow(NoteData noteData, NoteWindowManager windowManager)
    {
        InitializeComponent();

        NoteData = noteData;
        _windowManager = windowManager;
        _imageDirectory = AppPaths.GetNoteAssetsDirectory(noteData.Id);
        _attachmentDirectory = AppPaths.GetNoteAttachmentsDirectory(noteData.Id);
        _backgroundImageDirectory = AppPaths.GetNoteBackgroundDirectory(noteData.Id);

        PreviewHost.Content = _previewViewer;

        _bottomMostTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(120)
        };
        _bottomMostTimer.Tick += BottomMostTimer_OnTick;

        _previewDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(220)
        };
        _previewDebounceTimer.Tick += (_, _) =>
        {
            _previewDebounceTimer.Stop();
            RefreshPreviewStyleFromSettings();
        };

        _chromeTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(220)
        };
        _chromeTimer.Tick += (_, _) =>
        {
            _chromeTimer.Stop();
            UpdateChromeVisibility();
        };

        Opened += OnOpened;
        Closed += OnClosed;
        PositionChanged += OnPositionChanged;
        SizeChanged += OnSizeChanged;
        PointerEntered += OnPointerEntered;
        PointerExited += OnPointerExited;
        KeyDown += OnKeyDown;
        EditorTextBox.GotFocus += (_, _) => UpdateChromeVisibility();
        EditorTextBox.LostFocus += (_, _) => ScheduleChromeVisibilityUpdate();

        Width = noteData.Width > 0 ? noteData.Width : Width;
        Height = noteData.Height > 0 ? noteData.Height : Height;
        if (noteData.Left > 0 && noteData.Top > 0)
        {
            Position = new PixelPoint((int)Math.Round(noteData.Left), (int)Math.Round(noteData.Top));
        }

        _editorDisplayMode = noteData.LastEditorDisplayMode ?? InitialEditorMode();
        EditorTextBox.Text = noteData.Content;
        ApplyTheme(noteData.IsDarkMode);
        ApplyHeaderColor(noteData.TitleBarColor);
        ApplyBackgroundImage(noteData.BackgroundImagePath);
        BackgroundImage.Opacity = noteData.BackgroundImageOpacity;
        ApplyDisplayMode(_editorDisplayMode);
        RefreshTaskbarVisibilityFromSettings();
        RefreshChromeBehaviorFromSettings();
        UpdateHeaderInfo();
        UpdateIconState();
    }

    public NoteData NoteData { get; }

    public void ApplyWindowLevel(WindowLevel level)
    {
        Topmost = level == WindowLevel.TopMost;
        NoteData.Level = level;
        App.Services?.NoteManager.UpdateNote(NoteData);
        ApplyPlatformWindowLevel();
        RefreshTaskbarVisibilityFromSettings();
        UpdateHeaderInfo();
        UpdateIconState();
    }

    public void RefreshTaskbarVisibilityFromSettings()
    {
        var settingsStore = new SettingsStore();
        var modeValue = settingsStore.GetValue(
            FloatingWindowTaskbarVisibility.SettingKey,
            shouldSync: true,
            defaultValue: FloatingWindowTaskbarVisibility.DefaultValue);
        ShowInTaskbar = FloatingWindowTaskbarVisibility.ShouldShowInTaskbar(NoteData.Level, modeValue);
    }

    public void RefreshChromeBehaviorFromSettings()
    {
        _autoCollapseChromeEnabled = NoteWindowUiSettings.IsAutoCollapseChromeEnabled(new SettingsStore());
        if (!_autoCollapseChromeEnabled)
        {
            _chromeTimer.Stop();
        }

        UpdateChromeVisibility();
    }

    public void RefreshPreviewStyleFromSettings()
    {
        _previewViewer.Markdown = EditorTextBox.Text ?? string.Empty;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        _platformHandle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        ApplyPlatformWindowLevel();
        RefreshPreviewStyleFromSettings();
        RefreshChromeBehaviorFromSettings();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _bottomMostTimer.Stop();
        _previewDebounceTimer.Stop();
        _chromeTimer.Stop();
        _windowManager.OnNoteWindowClosed(NoteData);
    }

    private void OnPositionChanged(object? sender, PixelPointEventArgs e)
    {
        if (WindowState != WindowState.Normal)
        {
            return;
        }

        NoteData.Left = e.Point.X;
        NoteData.Top = e.Point.Y;
        App.Services?.NoteManager.UpdateNote(NoteData);
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (WindowState != WindowState.Normal)
        {
            return;
        }

        NoteData.Width = e.NewSize.Width;
        NoteData.Height = e.NewSize.Height;
        App.Services?.NoteManager.UpdateNote(NoteData);
    }

    private void OnPointerEntered(object? sender, PointerEventArgs e)
    {
        _isPointerInside = true;
        _chromeTimer.Stop();
        UpdateChromeVisibility();
    }

    private void OnPointerExited(object? sender, PointerEventArgs e)
    {
        _isPointerInside = false;
        ScheduleChromeVisibilityUpdate();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && NoteData.IsEditMode)
        {
            ApplyDisplayMode(EditorDisplayMode.PreviewOnly);
            e.Handled = true;
        }
    }

    private void BottomMostTimer_OnTick(object? sender, EventArgs e)
    {
        if (_platformHandle == IntPtr.Zero || NoteData.Level != WindowLevel.BottomMost)
        {
            return;
        }

        SetWindowPos(_platformHandle, HwndBottom, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    private void ApplyPlatformWindowLevel()
    {
        Topmost = NoteData.Level == WindowLevel.TopMost;
        if (_platformHandle == IntPtr.Zero)
        {
            return;
        }

        _bottomMostTimer.Stop();
        if (NoteData.Level == WindowLevel.BottomMost)
        {
            Topmost = false;
            SetWindowPos(_platformHandle, HwndNoTopMost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow);
            SetWindowPos(_platformHandle, HwndBottom, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow);
            _bottomMostTimer.Start();
            return;
        }

        SetWindowPos(_platformHandle, HwndNoTopMost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow);
    }

    private EditorDisplayMode InitialEditorMode()
    {
        if (string.IsNullOrWhiteSpace(NoteData.Content))
        {
            return EditorDisplayModeSettings.GetEnterMode(new SettingsStore());
        }

        return EditorDisplayMode.PreviewOnly;
    }

    private void ApplyDisplayMode(EditorDisplayMode mode)
    {
        _editorDisplayMode = mode;
        NoteData.LastEditorDisplayMode = mode;
        NoteData.IsEditMode = mode != EditorDisplayMode.PreviewOnly;

        EditorTextBox.IsVisible = mode != EditorDisplayMode.PreviewOnly;
        PreviewBorder.IsVisible = mode != EditorDisplayMode.TextOnly;
        EditorPreviewSplitter.IsVisible = mode == EditorDisplayMode.TextAndPreview;
        ContentGrid.ColumnDefinitions[0].Width = mode == EditorDisplayMode.PreviewOnly ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        ContentGrid.ColumnDefinitions[1].Width = mode == EditorDisplayMode.TextAndPreview ? new GridLength(6) : new GridLength(0);
        ContentGrid.ColumnDefinitions[2].Width = mode == EditorDisplayMode.TextOnly ? new GridLength(0) : new GridLength(1, GridUnitType.Star);

        UpdateHeaderInfo();
        UpdateIconState();
        UpdateChromeVisibility();
        App.Services?.NoteManager.UpdateNote(NoteData);
    }

    private void UpdateChromeVisibility()
    {
        var shouldShowChrome = ShouldShowChrome();
        if (_chromeExpanded == shouldShowChrome)
        {
            return;
        }

        _chromeExpanded = shouldShowChrome;
        HeaderBorder.Height = shouldShowChrome ? ExpandedHeaderHeight : 0;
        HeaderBorder.Opacity = shouldShowChrome ? 1 : 0;
        HeaderBorder.IsHitTestVisible = shouldShowChrome;
        ToolbarBorder.Height = shouldShowChrome ? ExpandedToolbarHeight : 0;
        ToolbarBorder.Opacity = shouldShowChrome ? 1 : 0;
        ToolbarBorder.IsHitTestVisible = shouldShowChrome;
    }

    private void ScheduleChromeVisibilityUpdate()
    {
        if (!_autoCollapseChromeEnabled)
        {
            UpdateChromeVisibility();
            return;
        }

        _chromeTimer.Stop();
        if (ShouldShowChrome())
        {
            UpdateChromeVisibility();
            return;
        }

        _chromeTimer.Start();
    }

    private bool ShouldShowChrome()
    {
        if (!_autoCollapseChromeEnabled)
        {
            return true;
        }

        var keepExpandedForEditor = NoteData.IsEditMode &&
                                    (EditorTextBox.IsKeyboardFocusWithin || EditorTextBox.IsPointerOver);
        var keepExpandedForContextMenu = HeaderBorder.ContextMenu?.IsOpen == true;
        return keepExpandedForEditor || keepExpandedForContextMenu || _isPointerInside;
    }

    private void UpdateHeaderInfo()
    {
        Title = string.IsNullOrWhiteSpace(NoteData.Title)
            ? $"便签 {NoteData.Id.ToString(CultureInfo.InvariantCulture)}"
            : NoteData.Title;
        TitleTextBlock.Text = Title;
        ToolTip.SetTip(TitleBarDragRegion, $"{Title}\n{GetLevelLabel(NoteData.Level)} · {GetModeLabel(_editorDisplayMode)}");
        ToolTip.SetTip(TitleGlyphIcon, $"{Title}\n{GetLevelLabel(NoteData.Level)}");
        ToolTip.SetTip(TitleTextBlock, Title);
    }

    private void UpdateIconState()
    {
        LevelIcon.Kind = GetLevelIcon(NoteData.Level);
        ToolTip.SetTip(LevelButton, $"切换窗口层级，当前：{GetLevelLabel(NoteData.Level)}");

        ModeIcon.Kind = GetModeIcon(_editorDisplayMode);
        ToolTip.SetTip(ModeButton, $"切换编辑器模式，当前：{GetModeLabel(_editorDisplayMode)}");

        ThemeIcon.Kind = NoteData.IsDarkMode ? PackIconMaterialKind.WhiteBalanceSunny : PackIconMaterialKind.WeatherNight;
        ToolTip.SetTip(ThemeButton, NoteData.IsDarkMode ? "切换到浅色模式" : "切换到深色模式");
    }

    private void ApplyTheme(bool darkMode)
    {
        NoteData.IsDarkMode = darkMode;
        RootBorder.Background = darkMode
            ? SolidColorBrush.Parse("#1F252B")
            : SolidColorBrush.Parse("#F8FAFC");
        RootBorder.BorderBrush = darkMode
            ? SolidColorBrush.Parse("#50565D")
            : SolidColorBrush.Parse("#D8DFE8");
        EditorTextBox.Background = darkMode
            ? SolidColorBrush.Parse("#20262C")
            : SolidColorBrush.Parse("#FFFFFFFF");
        EditorTextBox.Foreground = darkMode
            ? Brushes.White
            : SolidColorBrush.Parse("#243240");
        PreviewBorder.Background = darkMode
            ? SolidColorBrush.Parse("#20262C")
            : Brushes.White;
        ToolbarBorder.Background = darkMode
            ? SolidColorBrush.Parse("#283038")
            : SolidColorBrush.Parse("#F6F8FB");
        UpdateIconState();
        App.Services?.NoteManager.UpdateNote(NoteData);
    }

    private void ApplyHeaderColor(string? colorValue)
    {
        var value = string.IsNullOrWhiteSpace(colorValue) ? _headerColors[0] : colorValue;
        HeaderBorder.Background = Brush.Parse(value);
        NoteData.TitleBarColor = value;
    }

    private void ApplyBackgroundImage(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            BackgroundImage.Source = null;
            return;
        }

        try
        {
            BackgroundImage.Source = new Bitmap(imagePath);
        }
        catch
        {
            BackgroundImage.Source = null;
        }
    }

    private void QueuePreviewRefresh()
    {
        _previewDebounceTimer.Stop();
        _previewDebounceTimer.Start();
    }

    private async Task<string?> PickFileAsync(string title)
    {
        var result = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });
        return result.FirstOrDefault()?.TryGetLocalPath();
    }

    private static void SafeDelete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }

    private void InsertTextAtCaret(string text)
    {
        var currentText = EditorTextBox.Text ?? string.Empty;
        var caret = EditorTextBox.CaretIndex;
        EditorTextBox.Text = currentText.Insert(caret, text);
        EditorTextBox.CaretIndex = caret + text.Length;
        EditorTextBox.Focus();
    }

    private void InsertImage(string sourceFilePath)
    {
        string? destinationPath = null;
        try
        {
            var fileName = $"{Guid.NewGuid()}{Path.GetExtension(sourceFilePath)}";
            destinationPath = Path.Combine(_imageDirectory, fileName);
            File.Copy(sourceFilePath, destinationPath, true);
            var relativePath = $"note-assets/{NoteData.Id}/{fileName}";
            InsertTextAtCaret($"![{Path.GetFileNameWithoutExtension(sourceFilePath)}]({relativePath}){Environment.NewLine}");
        }
        catch (Exception ex)
        {
            SafeDelete(destinationPath);
            AppLogger.Warn($"Failed to insert image: {ex.Message}");
        }
    }

    private void InsertAttachment(string sourceFilePath)
    {
        try
        {
            var fileInfo = new FileInfo(sourceFilePath);
            var settingsStore = new SettingsStore();
            var syncAttachment = AttachmentSyncSettings.GetAutoSyncEnabled(settingsStore);
            var threshold = AttachmentSyncSettings.GetAutoSyncThresholdBytes(settingsStore);

            string linkTarget;
            if (syncAttachment && fileInfo.Length <= threshold)
            {
                var fileName = $"{Guid.NewGuid()}{Path.GetExtension(sourceFilePath)}";
                var destinationPath = Path.Combine(_attachmentDirectory, fileName);
                File.Copy(sourceFilePath, destinationPath, true);
                linkTarget = $"note-assets/attachments/{NoteData.Id}/{fileName}";
            }
            else
            {
                linkTarget = new Uri(sourceFilePath, UriKind.Absolute).AbsoluteUri;
            }

            InsertTextAtCaret($"[{Path.GetFileName(sourceFilePath)}]({linkTarget}){Environment.NewLine}");
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Failed to insert attachment: {ex.Message}");
        }
    }

    private void SetBackgroundImage(string sourceFilePath)
    {
        string? destinationPath = null;
        try
        {
            destinationPath = Path.Combine(_backgroundImageDirectory, $"background{Path.GetExtension(sourceFilePath)}");
            File.Copy(sourceFilePath, destinationPath, true);
            NoteData.BackgroundImagePath = destinationPath;
            ApplyBackgroundImage(destinationPath);
            App.Services?.NoteManager.UpdateNote(NoteData);
        }
        catch (Exception ex)
        {
            SafeDelete(destinationPath);
            AppLogger.Warn($"Failed to set background image: {ex.Message}");
        }
    }

    private void EditorTextBox_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        NoteData.Content = EditorTextBox.Text ?? string.Empty;
        App.Services?.NoteManager.UpdateNote(NoteData);
        QueuePreviewRefresh();
    }

    private void TitleBarDragRegion_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private async void RenameTitle_OnClick(object? sender, RoutedEventArgs e)
    {
        var newTitle = await DialogService.PromptTextAsync(this, "重命名便签", "输入新的便签标题", NoteData.Title);
        if (string.IsNullOrWhiteSpace(newTitle))
        {
            return;
        }

        var normalized = newTitle.Trim();
        var duplicateExists = App.Services?.NoteManager.Notes.Any(note =>
            note.Id != NoteData.Id &&
            string.Equals((note.Title ?? string.Empty).Trim(), normalized, StringComparison.OrdinalIgnoreCase)) == true;
        if (duplicateExists)
        {
            await DialogService.ShowMessageAsync(this, "标题重复", "已存在同名便签。");
            return;
        }

        NoteData.Title = normalized;
        UpdateHeaderInfo();
        App.Services?.NoteManager.UpdateNote(NoteData);
    }

    private void CycleWindowLevel_OnClick(object? sender, RoutedEventArgs e)
    {
        var nextLevel = NoteData.Level switch
        {
            WindowLevel.Normal => WindowLevel.TopMost,
            WindowLevel.TopMost => WindowLevel.BottomMost,
            _ => WindowLevel.Normal
        };
        ApplyWindowLevel(nextLevel);
    }

    private void CycleMode_OnClick(object? sender, RoutedEventArgs e)
    {
        var nextMode = _editorDisplayMode switch
        {
            EditorDisplayMode.TextOnly => EditorDisplayMode.TextAndPreview,
            EditorDisplayMode.TextAndPreview => EditorDisplayMode.PreviewOnly,
            _ => EditorDisplayMode.TextOnly
        };
        ApplyDisplayMode(nextMode);
    }

    private void ToggleTheme_OnClick(object? sender, RoutedEventArgs e)
    {
        ApplyTheme(!NoteData.IsDarkMode);
        QueuePreviewRefresh();
    }

    private async void DeleteNote_OnClick(object? sender, RoutedEventArgs e)
    {
        var shouldDelete = await DialogService.ShowConfirmationAsync(this, "删除便签", $"确定要删除“{Title}”吗？");
        if (!shouldDelete)
        {
            return;
        }

        App.Services?.NoteManager.DeleteNote(NoteData);
        Close();
    }

    private async void InsertImage_OnClick(object? sender, RoutedEventArgs e)
    {
        var file = await PickFileAsync("选择图片");
        if (file != null)
        {
            InsertImage(file);
        }
    }

    private async void InsertAttachment_OnClick(object? sender, RoutedEventArgs e)
    {
        var file = await PickFileAsync("选择附件");
        if (file != null)
        {
            InsertAttachment(file);
        }
    }

    private async void SelectBackground_OnClick(object? sender, RoutedEventArgs e)
    {
        var file = await PickFileAsync("选择背景图片");
        if (file != null)
        {
            SetBackgroundImage(file);
        }
    }

    private void ClearBackground_OnClick(object? sender, RoutedEventArgs e)
    {
        NoteData.BackgroundImagePath = null;
        BackgroundImage.Source = null;
        App.Services?.NoteManager.UpdateNote(NoteData);

        if (!Directory.Exists(_backgroundImageDirectory))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(_backgroundImageDirectory))
        {
            SafeDelete(file);
        }
    }

    private void ChangeHeaderColor_OnClick(object? sender, RoutedEventArgs e)
    {
        var currentIndex = Array.FindIndex(_headerColors, color => string.Equals(color, NoteData.TitleBarColor, StringComparison.OrdinalIgnoreCase));
        var nextIndex = (currentIndex + 1 + _headerColors.Length) % _headerColors.Length;
        var nextColor = _headerColors[nextIndex];
        ApplyHeaderColor(nextColor);
        App.Services?.NoteManager.UpdateNote(NoteData);
    }

    private void PreviewBorder_OnDoubleTapped(object? sender, RoutedEventArgs e)
    {
        ApplyDisplayMode(EditorDisplayMode.TextAndPreview);
        EditorTextBox.Focus();
    }

    private void CloseNote_OnClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private static string GetLevelLabel(WindowLevel level)
    {
        return level switch
        {
            WindowLevel.TopMost => "置顶窗口",
            WindowLevel.BottomMost => "置底窗口",
            _ => "普通窗口"
        };
    }

    private PackIconMaterialKind GetLevelIcon(WindowLevel level)
    {
        return level switch
        {
            WindowLevel.TopMost => PackIconMaterialKind.Pin,
            WindowLevel.BottomMost => PackIconMaterialKind.FormatVerticalAlignBottom,
            _ => PackIconMaterialKind.PinOffOutline
        };
    }

    private static string GetModeLabel(EditorDisplayMode mode)
    {
        return mode switch
        {
            EditorDisplayMode.TextOnly => "纯文本",
            EditorDisplayMode.TextAndPreview => "分栏预览",
            _ => "仅预览"
        };
    }

    private PackIconMaterialKind GetModeIcon(EditorDisplayMode mode)
    {
        return mode switch
        {
            EditorDisplayMode.TextOnly => PackIconMaterialKind.PencilOutline,
            EditorDisplayMode.TextAndPreview => PackIconMaterialKind.ViewSplitVertical,
            _ => PackIconMaterialKind.EyeOutline
        };
    }
}
