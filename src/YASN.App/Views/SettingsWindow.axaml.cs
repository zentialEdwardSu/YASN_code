using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using YASN.Settings;
using YASN.Sync;
using YASN.Sync.WebDav;

namespace YASN;

public partial class SettingsWindow : Window
{
    private readonly NoteWindowManager _windowManager;
    private readonly SyncManager? _syncManager;
    private SettingsStore _settingsStore = new();
    private readonly WindowNotificationManager _notificationManager;
    private Button[] _navigationButtons = [];
    private int _autoSaveSuppressionDepth;
    private bool _hasPendingSyncRuntimeApply;

    public SettingsWindow(NoteWindowManager windowManager, SyncManager? syncManager)
    {
        InitializeComponent();
        _windowManager = windowManager;
        _syncManager = syncManager;
        _notificationManager = new WindowNotificationManager(this)
        {
            Position = NotificationPosition.TopRight,
            MaxItems = 3
        };

        Closing += OnClosing;

        InitializeNavigation();
        LoadOptions();
        LoadValues();
    }

    private bool AutoSaveEnabled => _autoSaveSuppressionDepth == 0;

    private void LoadOptions()
    {
        TaskbarVisibilityComboBox.ItemsSource = new[]
        {
            new ComboOption("始终显示", FloatingWindowTaskbarVisibility.AlwaysShowValue),
            new ComboOption("始终隐藏", FloatingWindowTaskbarVisibility.AlwaysHideValue),
            new ComboOption("仅置顶窗口隐藏", FloatingWindowTaskbarVisibility.HideTopMostOnlyValue)
        };

        EditorModeComboBox.ItemsSource = new[]
        {
            new ComboOption("纯文本", EditorDisplayModeSettings.TextOnlyValue),
            new ComboOption("文本 + 预览", EditorDisplayModeSettings.TextAndPreviewValue),
            new ComboOption("仅预览", EditorDisplayModeSettings.PreviewOnlyValue)
        };
    }

    private void LoadValues()
    {
        RunWithoutAutoSave(() =>
        {
            AutoStartCheckBox.IsChecked = AutoStartManager.IsAutoStartEnabled();
            AutoCollapseChromeCheckBox.IsChecked = NoteWindowUiSettings.IsAutoCollapseChromeEnabled(_settingsStore);
            LogSizeTextBox.Text = _settingsStore.GetValue("log.maxSizeKb", shouldSync: true, defaultValue: "1024");
            DataDirectoryTextBox.Text = AppPaths.StorageRoot;

            SelectComboValue(
                TaskbarVisibilityComboBox,
                _settingsStore.GetValue(
                    FloatingWindowTaskbarVisibility.SettingKey,
                    shouldSync: true,
                    defaultValue: FloatingWindowTaskbarVisibility.DefaultValue));

            SelectComboValue(
                EditorModeComboBox,
                _settingsStore.GetValue(
                    EditorDisplayModeSettings.SettingKey,
                    shouldSync: false,
                    defaultValue: EditorDisplayModeSettings.DefaultValue));

            WebDavServerUrlTextBox.Text = _settingsStore.GetValue("webdav.server", shouldSync: false, defaultValue: "https://dav.jianguoyun.com/dav/");
            WebDavUserTextBox.Text = _settingsStore.GetValue("webdav.user", shouldSync: false);
            WebDavPasswordTextBox.Text = _settingsStore.GetValue("webdav.password", shouldSync: false);
            WebDavRemoteTextBox.Text = _settingsStore.GetValue("webdav.remote", shouldSync: false, defaultValue: "/YASN");
            SyncIntervalTextBox.Text = _settingsStore.GetValue("webdav.syncIntervalSeconds", shouldSync: true, defaultValue: "300");

            AutoSyncCheckBox.IsChecked =
                bool.TryParse(_settingsStore.GetValue("webdav.autoSync", shouldSync: false, defaultValue: bool.FalseString), out var autoSync)
                && autoSync;

            AttachmentAutoSyncCheckBox.IsChecked = AttachmentSyncSettings.GetAutoSyncEnabled(_settingsStore);
            AttachmentThresholdTextBox.Text = AttachmentSyncSettings.ParseThresholdMb(
                    _settingsStore.GetValue(
                        AttachmentSyncSettings.AutoSyncThresholdMbKey,
                        shouldSync: true,
                        defaultValue: AttachmentSyncSettings.DefaultAutoSyncThresholdMb.ToString(CultureInfo.InvariantCulture)))
                .ToString(CultureInfo.InvariantCulture);
        });
    }

    private void AutoStart_OnChanged(object? sender, RoutedEventArgs e)
    {
        if (!AutoSaveEnabled)
        {
            return;
        }

        var enabled = AutoStartCheckBox.IsChecked == true;
        var ok = enabled
            ? AutoStartManager.EnableAutoStart()
            : AutoStartManager.DisableAutoStart();

        if (!ok)
        {
            RunWithoutAutoSave(() => AutoStartCheckBox.IsChecked = !enabled);
            Notify("自动启动", "自动启动设置失败。", NotificationType.Error);
            return;
        }

        Notify("自动启动", enabled ? "已启用开机自动启动。" : "已关闭开机自动启动。", NotificationType.Success);
    }

    private void AutoCollapseChrome_OnChanged(object? sender, RoutedEventArgs e)
    {
        if (!AutoSaveEnabled)
        {
            return;
        }

        PersistToggle(
            NoteWindowUiSettings.AutoCollapseChromeKey,
            shouldSync: true,
            AutoCollapseChromeCheckBox.IsChecked == true);

        _windowManager.RefreshChromeBehavior();
        Notify("浮窗行为", "标题栏自动收起设置已保存。", NotificationType.Success);
    }

    private void TaskbarVisibility_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!AutoSaveEnabled)
        {
            return;
        }

        if (SelectedComboValue(TaskbarVisibilityComboBox) is not { } mode)
        {
            return;
        }

        PersistText(
            FloatingWindowTaskbarVisibility.SettingKey,
            shouldSync: true,
            FloatingWindowTaskbarVisibility.NormalizeValue(mode),
            FloatingWindowTaskbarVisibility.DefaultValue);

        _windowManager.RefreshTaskbarVisibility();
        Notify("任务栏", "任务栏显示策略已保存。", NotificationType.Success);
    }

    private void LogSize_OnLostFocus(object? sender, RoutedEventArgs e)
    {
        if (!AutoSaveEnabled)
        {
            return;
        }

        var normalized = ParsePositiveInt(LogSizeTextBox.Text, 1024);
        RunWithoutAutoSave(() => LogSizeTextBox.Text = normalized.ToString(CultureInfo.InvariantCulture));

        var changed = PersistTextIfChanged(
            "log.maxSizeKb",
            shouldSync: true,
            normalized.ToString(CultureInfo.InvariantCulture),
            "1024");

        Logging.AppLogger.SetMaxSizeKb(normalized);

        if (changed)
        {
            Notify("日志", $"日志大小上限已更新为 {normalized} KB。", NotificationType.Success);
        }
    }

    private void EditorMode_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!AutoSaveEnabled)
        {
            return;
        }

        if (SelectedComboValue(EditorModeComboBox) is not { } mode)
        {
            return;
        }

        PersistText(
            EditorDisplayModeSettings.SettingKey,
            shouldSync: false,
            mode,
            EditorDisplayModeSettings.DefaultValue);

        Notify("编辑器", "默认编辑模式已保存。", NotificationType.Success);
    }

    private async void BrowseDataDirectory_OnClick(object? sender, RoutedEventArgs e)
    {
        var startFolder = await StorageProvider.TryGetFolderFromPathAsync(AppPaths.StorageRoot);
        var result = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择存储根目录",
            SuggestedStartLocation = startFolder,
            AllowMultiple = false
        });

        var folder = result.FirstOrDefault();
        if (folder?.TryGetLocalPath() is { } path)
        {
            DataDirectoryTextBox.Text = path;
        }
    }

    private async void ApplyDataDirectory_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!AppPaths.TryNormalizeStorageRoot(DataDirectoryTextBox.Text, out var normalizedRoot, out var errorMessage))
        {
            await DialogService.ShowMessageAsync(this, "无效目录", $"存储根目录无效：{errorMessage}");
            Notify("存储根目录", "目录校验失败。", NotificationType.Error);
            return;
        }

        var previousRoot = AppPaths.StorageRoot;
        var previousDataDirectory = AppPaths.DataDirectory;
        var previousLocalSettings = AppPaths.LocalSettingsPath;
        var previousLegacyLocalSettings = AppPaths.LegacyLocalSettingsPath;
        var previousLogPath = AppPaths.LogFilePath;

        AppPaths.ApplyStorageRoot(normalizedRoot);
        try
        {
            MigrateStorageLayout(
                previousRoot,
                previousDataDirectory,
                previousLocalSettings,
                previousLegacyLocalSettings,
                previousLogPath,
                normalizedRoot);
        }
        catch (Exception ex)
        {
            AppPaths.ApplyStorageRoot(previousRoot);
            await DialogService.ShowMessageAsync(this, "迁移失败", $"切换存储根目录失败：{ex.Message}");
            Notify("存储根目录", "迁移失败，已保留原目录。", NotificationType.Error);
            return;
        }

        _settingsStore = new SettingsStore();
        LoadValues();
        _windowManager.RefreshTaskbarVisibility();
        _windowManager.RefreshChromeBehavior();
        Notify("存储根目录", $"已切换到 {normalizedRoot}，数据位于 {AppPaths.DataDirectory}。", NotificationType.Information);
    }

    private async void ExportSettings_OnClick(object? sender, RoutedEventArgs e)
    {
        var result = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "导出设置",
            SuggestedFileName = $"yasn-settings-{DateTime.Now:yyyyMMdd-HHmmss}.json"
        });

        var path = result?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (_settingsStore.ExportToFile(path, out var errorMessage))
        {
            Notify("设置导出", $"设置已导出到 {path}。", NotificationType.Success);
            return;
        }

        Notify("设置导出", $"导出失败：{errorMessage}", NotificationType.Error);
    }

    private async void ImportSettings_OnClick(object? sender, RoutedEventArgs e)
    {
        var result = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "导入设置",
            AllowMultiple = false
        });

        var path = result.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (_settingsStore.ImportFromFile(path, out var errorMessage))
        {
            LoadValues();
            _windowManager.RefreshTaskbarVisibility();
            _windowManager.RefreshChromeBehavior();
            Notify("设置导入", "设置已导入。", NotificationType.Success);
            return;
        }

        Notify("设置导入", $"导入失败：{errorMessage}", NotificationType.Error);
    }

    private void WebDavField_OnLostFocus(object? sender, RoutedEventArgs e)
    {
        if (!AutoSaveEnabled)
        {
            return;
        }

        if (PersistWebDavDraft())
        {
            _hasPendingSyncRuntimeApply = true;
            Notify("同步草稿", "WebDAV 设置已自动保存。", NotificationType.Success);
        }
    }

    private void WebDavToggle_OnChanged(object? sender, RoutedEventArgs e)
    {
        if (!AutoSaveEnabled)
        {
            return;
        }

        if (PersistWebDavDraft())
        {
            _hasPendingSyncRuntimeApply = true;
            Notify("同步草稿", "同步策略已自动保存。", NotificationType.Success);
        }
    }

    private async void TestWebDav_OnClick(object? sender, RoutedEventArgs e)
    {
        PersistWebDavDraft();

        using var client = new WebDavSyncClient(BuildWebDavOptions());
        var remoteDirectory = NormalizeDirectory(WebDavRemoteTextBox.Text);
        var ok = await client.TestConnectionAsync(remoteDirectory);

        Notify(
            "WebDAV 连接",
            ok ? "连接成功。" : $"连接失败：{client.LastError ?? "未知错误"}",
            ok ? NotificationType.Success : NotificationType.Error);
    }

    private async void SaveWebDav_OnClick(object? sender, RoutedEventArgs e)
    {
        await ApplyRuntimeSettingsAsync(forceSyncApply: true, notifyResult: true);
    }

    private bool PersistWebDavDraft()
    {
        var intervalSeconds = ParsePositiveInt(SyncIntervalTextBox.Text, 300);
        var thresholdMb = AttachmentSyncSettings.ParseThresholdMb(AttachmentThresholdTextBox.Text);

        RunWithoutAutoSave(() =>
        {
            SyncIntervalTextBox.Text = intervalSeconds.ToString(CultureInfo.InvariantCulture);
            AttachmentThresholdTextBox.Text = thresholdMb.ToString(CultureInfo.InvariantCulture);
        });

        var changed = false;
        changed |= PersistTextIfChanged("webdav.server", shouldSync: false, WebDavServerUrlTextBox.Text, string.Empty);
        changed |= PersistTextIfChanged("webdav.user", shouldSync: false, WebDavUserTextBox.Text, string.Empty);
        changed |= PersistTextIfChanged("webdav.password", shouldSync: false, WebDavPasswordTextBox.Text, string.Empty, trim: false);
        changed |= PersistTextIfChanged("webdav.remote", shouldSync: false, WebDavRemoteTextBox.Text, "/YASN");
        changed |= PersistTextIfChanged("webdav.syncIntervalSeconds", shouldSync: true, intervalSeconds.ToString(CultureInfo.InvariantCulture), "300");
        changed |= PersistToggleIfChanged("webdav.autoSync", shouldSync: false, AutoSyncCheckBox.IsChecked == true, defaultValue: false);
        changed |= PersistToggleIfChanged(
            AttachmentSyncSettings.AutoSyncEnabledKey,
            shouldSync: true,
            AttachmentAutoSyncCheckBox.IsChecked == true,
            defaultValue: AttachmentSyncSettings.DefaultAutoSyncEnabled);
        changed |= PersistTextIfChanged(
            AttachmentSyncSettings.AutoSyncThresholdMbKey,
            shouldSync: true,
            thresholdMb.ToString(CultureInfo.InvariantCulture),
            AttachmentSyncSettings.DefaultAutoSyncThresholdMb.ToString(CultureInfo.InvariantCulture));

        return changed;
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        _ = ApplySettingsOnCloseAsync();
    }

    private async Task ApplySettingsOnCloseAsync()
    {
        try
        {
            await ApplyRuntimeSettingsAsync(forceSyncApply: false, notifyResult: false);
        }
        catch (Exception ex)
        {
            Logging.AppLogger.Warn($"Failed to hot-apply settings on close: {ex.Message}");
        }
    }

    private async Task ApplyRuntimeSettingsAsync(bool forceSyncApply, bool notifyResult)
    {
        ApplyLocalRuntimeSettings();

        var draftChanged = PersistWebDavDraft();
        var shouldApplySyncRuntime = forceSyncApply || draftChanged || _hasPendingSyncRuntimeApply;
        var syncDraft = CaptureSyncDraft();

        if (!shouldApplySyncRuntime)
        {
            return;
        }

        var configured = await ApplySyncRuntimeSettingsAsync(syncDraft, notifyResult);
        if (configured)
        {
            _hasPendingSyncRuntimeApply = false;
        }
    }

    private void ApplyLocalRuntimeSettings()
    {
        PersistToggle(
            NoteWindowUiSettings.AutoCollapseChromeKey,
            shouldSync: true,
            AutoCollapseChromeCheckBox.IsChecked == true);

        if (SelectedComboValue(TaskbarVisibilityComboBox) is { } taskbarMode)
        {
            PersistText(
                FloatingWindowTaskbarVisibility.SettingKey,
                shouldSync: true,
                FloatingWindowTaskbarVisibility.NormalizeValue(taskbarMode),
                FloatingWindowTaskbarVisibility.DefaultValue);
        }

        if (SelectedComboValue(EditorModeComboBox) is { } editorMode)
        {
            PersistText(
                EditorDisplayModeSettings.SettingKey,
                shouldSync: false,
                editorMode,
                EditorDisplayModeSettings.DefaultValue);
        }

        var logSize = ParsePositiveInt(LogSizeTextBox.Text, 1024);
        RunWithoutAutoSave(() => LogSizeTextBox.Text = logSize.ToString(CultureInfo.InvariantCulture));
        PersistText("log.maxSizeKb", shouldSync: true, logSize.ToString(CultureInfo.InvariantCulture), "1024");
        Logging.AppLogger.SetMaxSizeKb(logSize);

        _windowManager.RefreshTaskbarVisibility();
        _windowManager.RefreshChromeBehavior();
    }

    private SyncDraft CaptureSyncDraft()
    {
        return new SyncDraft(
            BuildWebDavOptions(),
            NormalizeDirectory(WebDavRemoteTextBox.Text),
            AutoSyncCheckBox.IsChecked == true,
            ParsePositiveInt(SyncIntervalTextBox.Text, 300));
    }

    private async Task<bool> ApplySyncRuntimeSettingsAsync(SyncDraft draft, bool notifyResult)
    {
        if (_syncManager == null)
        {
            if (notifyResult)
            {
                Notify("同步设置", "设置已保存，但同步管理器尚未初始化。", NotificationType.Information);
            }

            return false;
        }

        var client = new WebDavSyncClient(draft.Options);
        var configured = await _syncManager.ConfigureAsync(
            client,
            draft.RemoteDirectory,
            draft.EnableAutoSync,
            draft.IntervalSeconds);

        _syncManager.SetIntervalSeconds(draft.IntervalSeconds);

        if (notifyResult)
        {
            Notify(
                "同步设置",
                configured ? "同步设置已应用。" : "同步设置应用失败。",
                configured ? NotificationType.Success : NotificationType.Error);
        }

        return configured;
    }

    private static int ParsePositiveInt(string? value, int fallback)
    {
        return int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;
    }

    private WebDavOptions BuildWebDavOptions()
    {
        return new WebDavOptions
        {
            ServerUrl = WebDavServerUrlTextBox.Text?.Trim() ?? string.Empty,
            Username = WebDavUserTextBox.Text?.Trim() ?? string.Empty,
            Password = WebDavPasswordTextBox.Text ?? string.Empty
        };
    }

    private static string NormalizeDirectory(string? rawPath)
    {
        return (rawPath ?? string.Empty).Trim().Trim('/');
    }

    private static void MigrateStorageLayout(
        string previousRoot,
        string previousDataDirectory,
        string previousLocalSettings,
        string previousLegacyLocalSettings,
        string previousLogPath,
        string newRoot)
    {
        Directory.CreateDirectory(newRoot);
        Directory.CreateDirectory(AppPaths.DataDirectory);

        if (!PathsEqual(previousDataDirectory, AppPaths.DataDirectory))
        {
            CopyDirectoryContents(previousDataDirectory, AppPaths.DataDirectory);
        }

        var localSettings = LoadSettingsDictionary(previousLocalSettings);
        if (localSettings.Count == 0)
        {
            localSettings = LoadSettingsDictionary(previousLegacyLocalSettings);
        }

        localSettings[AppPaths.DataDirectorySettingKey] = newRoot;
        SaveSettingsDictionary(localSettings, AppPaths.LocalSettingsPath);
        AppPaths.WriteBootstrapSettings(newRoot);

        if (!PathsEqual(previousLogPath, AppPaths.LogFilePath) && File.Exists(previousLogPath) && !File.Exists(AppPaths.LogFilePath))
        {
            File.Copy(previousLogPath, AppPaths.LogFilePath, overwrite: false);
        }
    }

    private static Dictionary<string, string> LoadSettingsDictionary(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                   ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void SaveSettingsDictionary(Dictionary<string, string> settings, string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    private static void CopyDirectoryContents(string sourceDirectory, string destinationDirectory)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            return;
        }

        foreach (var directory in Directory.GetDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, directory);
            Directory.CreateDirectory(Path.Combine(destinationDirectory, relativePath));
        }

        foreach (var file in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, file);
            var destinationPath = Path.Combine(destinationDirectory, relativePath);
            var destinationParent = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationParent))
            {
                Directory.CreateDirectory(destinationParent);
            }

            File.Copy(file, destinationPath, overwrite: true);
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }

    private void PersistText(string key, bool shouldSync, string? value, string defaultValue)
    {
        _settingsStore.PersistField(new SettingField
        {
            Key = key,
            ShouldSync = shouldSync,
            FieldType = SettingFieldType.Text,
            Value = NormalizeText(value, defaultValue, trim: true)
        });
    }

    private bool PersistTextIfChanged(string key, bool shouldSync, string? value, string defaultValue, bool trim = true)
    {
        var normalized = NormalizeText(value, defaultValue, trim);
        var current = NormalizeText(_settingsStore.GetValue(key, shouldSync, defaultValue), defaultValue, trim);

        if (string.Equals(current, normalized, StringComparison.Ordinal))
        {
            return false;
        }

        _settingsStore.PersistField(new SettingField
        {
            Key = key,
            ShouldSync = shouldSync,
            FieldType = SettingFieldType.Text,
            Value = normalized
        });

        return true;
    }

    private void PersistToggle(string key, bool shouldSync, bool value)
    {
        _settingsStore.PersistField(new SettingField
        {
            Key = key,
            ShouldSync = shouldSync,
            FieldType = SettingFieldType.Toggle,
            BoolValue = value
        });
    }

    private bool PersistToggleIfChanged(string key, bool shouldSync, bool value, bool defaultValue)
    {
        var currentRaw = _settingsStore.GetValue(key, shouldSync, defaultValue.ToString());
        var current = bool.TryParse(currentRaw, out var parsed) ? parsed : defaultValue;

        if (current == value)
        {
            return false;
        }

        PersistToggle(key, shouldSync, value);
        return true;
    }

    private static string NormalizeText(string? value, string defaultValue, bool trim)
    {
        if (value == null)
        {
            return defaultValue;
        }

        var normalized = trim ? value.Trim() : value;
        return string.IsNullOrWhiteSpace(normalized) ? defaultValue : normalized;
    }

    private static void SelectComboValue(ComboBox comboBox, string value)
    {
        if (comboBox.ItemsSource is not IEnumerable<ComboOption> options)
        {
            return;
        }

        comboBox.SelectedItem = options.FirstOrDefault(option => option.Value == value);
    }

    private static string? SelectedComboValue(ComboBox comboBox)
    {
        return (comboBox.SelectedItem as ComboOption)?.Value;
    }

    private void InitializeNavigation()
    {
        _navigationButtons =
        [
            GeneralNavButton,
            StorageNavButton,
            EditorNavButton,
            SyncNavButton
        ];
    }

    private void NavigateGeneral_OnClick(object? sender, RoutedEventArgs e)
    {
        NavigateToSection(GeneralNavButton, GeneralSection);
    }

    private void NavigateStorage_OnClick(object? sender, RoutedEventArgs e)
    {
        NavigateToSection(StorageNavButton, StorageSection);
    }

    private void NavigateEditor_OnClick(object? sender, RoutedEventArgs e)
    {
        NavigateToSection(EditorNavButton, EditorSection);
    }

    private void NavigateSync_OnClick(object? sender, RoutedEventArgs e)
    {
        NavigateToSection(SyncNavButton, SyncSection);
    }

    private void NavigateToSection(Button navigationButton, Control section)
    {
        foreach (var button in _navigationButtons)
        {
            button.Classes.Set("active", ReferenceEquals(button, navigationButton));
        }

        section.BringIntoView();
    }

    private void Notify(string title, string message, NotificationType type)
    {
        _notificationManager.Show(new Notification(title, message, type, TimeSpan.FromSeconds(3)));
    }

    private void RunWithoutAutoSave(Action action)
    {
        _autoSaveSuppressionDepth += 1;
        try
        {
            action();
        }
        finally
        {
            _autoSaveSuppressionDepth -= 1;
        }
    }

    private sealed record ComboOption(string Label, string Value)
    {
        public override string ToString()
        {
            return Label;
        }
    }

    private sealed record SyncDraft(
        WebDavOptions Options,
        string RemoteDirectory,
        bool EnableAutoSync,
        int IntervalSeconds);
}




