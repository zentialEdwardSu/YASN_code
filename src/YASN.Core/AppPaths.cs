using System.IO;
using System.Text.Json;

namespace YASN
{
    /// <summary>
    /// Centralized paths for app data and configuration storage.
    /// </summary>
    public static class AppPaths
    {
        public const string DataDirectorySettingKey = "app.dataDirectory";
        private const string DataFolderName = "data";
        private const string BootstrapFileName = "settings.local.json";
        private const string LocalSettingsFileName = "local.json";
        private const string LegacyLocalSettingsFileName = "settings.local.json";

        public static string BaseDirectory { get; } = AppDomain.CurrentDomain.BaseDirectory;
        public static string StorageRoot { get; private set; } = string.Empty;
        public static string DataDirectory { get; private set; } = string.Empty;
        public static string LegacyNotesFilePath => Path.Combine(DataDirectory, "notes.json");
        public static string NotesIndexPath => Path.Combine(DataDirectory, "notes.index.json");
        public static string NotesMarkdownRoot => Path.Combine(DataDirectory, "notes");
        public static string NoteAssetsRoot => Path.Combine(DataDirectory, "note-assets");
        public static string NoteAttachmentsRoot => Path.Combine(NoteAssetsRoot, "attachments");
        public static string NoteBackgroundsRoot => Path.Combine(NoteAssetsRoot, "backgrounds");
        public static string StyleRoot => Path.Combine(DataDirectory, "style");
        public static string HtmlCacheRoot => Path.Combine(DataDirectory, "html-cache");

        public static string SyncSettingsPath => Path.Combine(DataDirectory, "settings.sync.json");
        public static string LocalSettingsPath => Path.Combine(StorageRoot, LocalSettingsFileName);
        public static string LegacyLocalSettingsPath => Path.Combine(StorageRoot, LegacyLocalSettingsFileName);
        public static string BootstrapSettingsPath => Path.Combine(BaseDirectory, BootstrapFileName);
        public static string LogFilePath => Path.Combine(StorageRoot, "yasn_log.log");
        public static string SignatureFilePath => Path.Combine(DataDirectory, "sync.manifest.json");

        static AppPaths()
        {
            ApplyStorageRoot(ResolveStorageRoot());
        }

        public static bool TryNormalizeStorageRoot(string? value, out string normalizedPath, out string errorMessage)
        {
            errorMessage = string.Empty;
            try
            {
                var raw = string.IsNullOrWhiteSpace(value)
                    ? BaseDirectory
                    : value.Trim();

                var candidate = Path.GetFullPath(Path.IsPathRooted(raw)
                    ? raw
                    : Path.Combine(BaseDirectory, raw));

                if (string.Equals(Path.GetFileName(candidate), DataFolderName, StringComparison.OrdinalIgnoreCase) &&
                    Directory.GetParent(candidate) is { } parent)
                {
                    candidate = parent.FullName;
                }

                normalizedPath = candidate;
                Directory.CreateDirectory(normalizedPath);
                Directory.CreateDirectory(GetDataDirectoryForRoot(normalizedPath));
                return true;
            }
            catch (Exception ex)
            {
                normalizedPath = string.Empty;
                errorMessage = ex.Message;
                return false;
            }
        }

        public static bool TryNormalizeDataDirectory(string? value, out string normalizedPath, out string errorMessage)
        {
            if (TryNormalizeStorageRoot(value, out var storageRoot, out errorMessage))
            {
                normalizedPath = GetDataDirectoryForRoot(storageRoot);
                return true;
            }

            normalizedPath = string.Empty;
            return false;
        }

        public static string GetDataDirectoryForRoot(string storageRoot)
        {
            return Path.Combine(storageRoot, DataFolderName);
        }

        public static void ApplyStorageRoot(string storageRoot)
        {
            StorageRoot = storageRoot;
            DataDirectory = GetDataDirectoryForRoot(storageRoot);
            EnsureDirectories();
        }

        private static void EnsureDirectories()
        {
            Directory.CreateDirectory(StorageRoot);
            Directory.CreateDirectory(DataDirectory);
            Directory.CreateDirectory(NotesMarkdownRoot);
            Directory.CreateDirectory(NoteAssetsRoot);
            Directory.CreateDirectory(NoteAttachmentsRoot);
            Directory.CreateDirectory(NoteBackgroundsRoot);
            Directory.CreateDirectory(StyleRoot);
            Directory.CreateDirectory(HtmlCacheRoot);
        }

        private static string ResolveStorageRoot()
        {
            foreach (var path in new[]
                     {
                         BootstrapSettingsPath,
                         Path.Combine(BaseDirectory, LocalSettingsFileName),
                         Path.Combine(BaseDirectory, LegacyLocalSettingsFileName)
                     })
            {
                if (!TryReadStorageRoot(path, out var storageRoot))
                {
                    continue;
                }

                return storageRoot;
            }

            TryNormalizeStorageRoot(null, out var defaultPath, out _);
            return defaultPath;
        }

        private static bool TryReadStorageRoot(string path, out string storageRoot)
        {
            storageRoot = string.Empty;
            if (!File.Exists(path))
            {
                return false;
            }

            try
            {
                var json = File.ReadAllText(path);
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (dict != null && dict.TryGetValue(DataDirectorySettingKey, out var value) &&
                    TryNormalizeStorageRoot(value, out var configuredPath, out _))
                {
                    storageRoot = configuredPath;
                    return true;
                }
            }
            catch
            {
                // ignore malformed files and fallback to the next candidate
            }

            return false;
        }

        public static void WriteBootstrapSettings(string storageRoot)
        {
            var payload = new Dictionary<string, string>
            {
                [DataDirectorySettingKey] = storageRoot
            };

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(BootstrapSettingsPath, json);
        }

        public static string GetNoteMarkdownPath(int noteId)
        {
            return Path.Combine(NotesMarkdownRoot, $"{noteId}.md");
        }

        public static string GetNoteAssetsDirectory(int noteId)
        {
            var path = Path.Combine(NoteAssetsRoot, noteId.ToString());
            Directory.CreateDirectory(path);
            return path;
        }

        public static string GetNoteBackgroundDirectory(int noteId)
        {
            var path = Path.Combine(NoteBackgroundsRoot, noteId.ToString());
            Directory.CreateDirectory(path);
            return path;
        }

        public static string GetNoteAttachmentsDirectory(int noteId)
        {
            var path = Path.Combine(NoteAttachmentsRoot, noteId.ToString());
            Directory.CreateDirectory(path);
            return path;
        }

        public static string GetNoteHtmlCachePath(int noteId)
        {
            return Path.Combine(HtmlCacheRoot, $"{noteId}.html");
        }
    }
}
