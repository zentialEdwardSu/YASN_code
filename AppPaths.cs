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

        public static string BaseDirectory { get; } = AppDomain.CurrentDomain.BaseDirectory;
        public static string DataDirectory { get; }
        public static string LegacyNotesFilePath => Path.Combine(DataDirectory, "notes.json");
        public static string NotesIndexPath => Path.Combine(DataDirectory, "notes.index.json");
        public static string NotesMarkdownRoot => Path.Combine(DataDirectory, "notes");
        public static string NoteAssetsRoot => Path.Combine(DataDirectory, "note-assets");
        public static string NoteAttachmentsRoot => Path.Combine(NoteAssetsRoot, "attachments");
        public static string NoteBackgroundsRoot => Path.Combine(NoteAssetsRoot, "backgrounds");
        public static string StyleRoot => Path.Combine(DataDirectory, "style");
        public static string HtmlCacheRoot => Path.Combine(DataDirectory, "html-cache");

        public static string SyncSettingsPath => Path.Combine(DataDirectory, "settings.sync.json");
        public static string LocalSettingsPath { get; } = Path.Combine(BaseDirectory, "settings.local.json");
        public static string LogFilePath { get; } = Path.Combine(BaseDirectory, "yasn_log.log");
        public static string SignatureFilePath => Path.Combine(DataDirectory, "sync.manifest.json");

        static AppPaths()
        {
            DataDirectory = ResolveDataDirectory();
            Directory.CreateDirectory(DataDirectory);
            Directory.CreateDirectory(NotesMarkdownRoot);
            Directory.CreateDirectory(NoteAssetsRoot);
            Directory.CreateDirectory(NoteAttachmentsRoot);
            Directory.CreateDirectory(NoteBackgroundsRoot);
            Directory.CreateDirectory(StyleRoot);
            Directory.CreateDirectory(HtmlCacheRoot);
        }

        public static bool TryNormalizeDataDirectory(string? value, out string normalizedPath, out string errorMessage)
        {
            errorMessage = string.Empty;
            try
            {
                var raw = string.IsNullOrWhiteSpace(value)
                    ? Path.Combine(BaseDirectory, "data")
                    : value.Trim();

                normalizedPath = Path.GetFullPath(Path.IsPathRooted(raw)
                    ? raw
                    : Path.Combine(BaseDirectory, raw));

                Directory.CreateDirectory(normalizedPath);
                return true;
            }
            catch (Exception ex)
            {
                normalizedPath = string.Empty;
                errorMessage = ex.Message;
                return false;
            }
        }

        private static string ResolveDataDirectory()
        {
            if (File.Exists(LocalSettingsPath))
            {
                try
                {
                    var json = File.ReadAllText(LocalSettingsPath);
                    var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    if (dict != null && dict.TryGetValue(DataDirectorySettingKey, out var value) &&
                        TryNormalizeDataDirectory(value, out var configuredPath, out _))
                    {
                        return configuredPath;
                    }
                }
                catch
                {
                    // ignore malformed local settings and fallback to default path
                }
            }

            TryNormalizeDataDirectory(null, out var defaultPath, out _);
            return defaultPath;
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
