using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Toolkit.Uwp.Notifications;

namespace YASN.Logging
{
    /// <summary>
    /// Simple layered logger with file output and toast notifications for info/warn.
    /// </summary>
    public static class AppLogger
    {
        private static readonly object Lock = new();
        private static long _maxBytes = 1024 * 1024; // default 1 MB
        private static int _toastExpirationSeconds = 8; // default 8 seconds

        static AppLogger()
        {
            LoadMaxSizeFromLocalSettings();
        }

        public static void SetMaxSizeKb(int kb)
        {
            if (kb <= 0)
                return;

            _maxBytes = kb * 1024L;
        }

        public static void Debug(string message) => Write("DEBUG", message, showToast: false);

        public static void DebugToast(string message) => Write("DEBUG", message, showToast: true);

        public static void Info(string message) => Write("INFO", message, showToast: true);

        public static void Warn(string message) => Write("WARN", message, showToast: true);

        public static void SetToastExpirationSeconds(int seconds)
        {
            if (seconds <= 0)
                return;

            _toastExpirationSeconds = Math.Clamp(seconds, 1, 120);
        }

        private static void Write(string level, string message, bool showToast)
        {
            try
            {
                string line;
                var logPath = AppPaths.LogFilePath;
                lock (Lock)
                {
                    EnsureDirectory(logPath);
                    RotateIfNeeded(logPath);

                    line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
                    File.AppendAllLines(logPath, [line]);
                }

                WriteToTerminalInDebug(line);

                if (showToast)
                {
                    ShowToast(level, message);
                }
            }
            catch
            {
                // Avoid throwing from logger
            }
        }

        private static void WriteToTerminalInDebug(string line)
        {
#if DEBUG
            try
            {
                Console.WriteLine(line);
            }
            catch
            {
                // ignore terminal output failures
            }
#endif
        }

        private static void EnsureDirectory(string logPath)
        {
            var dir = Path.GetDirectoryName(logPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }

        private static void RotateIfNeeded(string logPath)
        {
            try
            {
                if (File.Exists(logPath))
                {
                    var info = new FileInfo(logPath);
                    if (info.Length >= _maxBytes)
                    {
                        var bakPath = logPath + ".bak";
                        if (File.Exists(bakPath))
                        {
                            File.Delete(bakPath);
                        }
                        File.Move(logPath, bakPath);
                    }
                }
            }
            catch
            {
                // ignore rotation failures
            }
        }

        private static void ShowToast(string level, string message)
        {
            try
            {
                _ = (level, message, _toastExpirationSeconds);
            }
            catch
            {
                // Toast failures should not crash the app
            }
        }

        private static (string Icon, string Title) GetToastHeader(string level)
        {
            return level switch
            {
                "INFO" => ("\u2139", "Info"),
                "WARN" => ("\u26A0", "Warning"),
                "DEBUG" => ("\u2699", "Debug"),
                _ => ("\u2022", level)
            };
        }

        private static void LoadMaxSizeFromLocalSettings()
        {
            try
            {
                foreach (var path in new[]
                         {
                             AppPaths.LocalSettingsPath,
                             AppPaths.LegacyLocalSettingsPath,
                             AppPaths.BootstrapSettingsPath
                         }.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (!File.Exists(path))
                    {
                        continue;
                    }

                    var json = File.ReadAllText(path);
                    var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    if (dict != null && dict.TryGetValue("log.maxSizeKb", out var value) &&
                        int.TryParse(value, out var kb) && kb > 0)
                    {
                        SetMaxSizeKb(kb);
                    }

                    if (dict != null && dict.TryGetValue("log.toastExpirationSeconds", out var toastSeconds) &&
                        int.TryParse(toastSeconds, out var seconds) && seconds > 0)
                    {
                        SetToastExpirationSeconds(seconds);
                    }

                    if (dict != null)
                    {
                        return;
                    }
                }
            }
            catch
            {
                // ignore
            }
        }
    }
}
