namespace YASN.Settings
{
    public static class AttachmentSyncSettings
    {
        public const string AutoSyncEnabledKey = "attachment.autoSyncEnabled";
        public const string AutoSyncThresholdMbKey = "attachment.autoSyncThresholdMb";
        public const bool DefaultAutoSyncEnabled = true;
        public const int DefaultAutoSyncThresholdMb = 5;

        public static bool GetAutoSyncEnabled(SettingsStore settingsStore)
        {
            var raw = settingsStore.GetValue(AutoSyncEnabledKey, shouldSync: false, DefaultAutoSyncEnabled.ToString());
            return !bool.TryParse(raw, out var value) || value;
        }

        private static int GetAutoSyncThresholdMb(SettingsStore settingsStore)
        {
            var raw = settingsStore.GetValue(AutoSyncThresholdMbKey, shouldSync: false, DefaultAutoSyncThresholdMb.ToString());
            return ParseThresholdMb(raw);
        }

        public static long GetAutoSyncThresholdBytes(SettingsStore settingsStore)
        {
            return (long)GetAutoSyncThresholdMb(settingsStore) * 1024 * 1024;
        }

        public static int ParseThresholdMb(string? value)
        {
            const int minMb = 1;
            const int maxMb = 1024;
            if (int.TryParse(value, out var mb))
            {
                if (mb < minMb)
                {
                    return minMb;
                }

                if (mb > maxMb)
                {
                    return maxMb;
                }

                return mb;
            }

            return DefaultAutoSyncThresholdMb;
        }
    }
}
