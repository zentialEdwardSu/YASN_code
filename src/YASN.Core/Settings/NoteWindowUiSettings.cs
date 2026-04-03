namespace YASN.Settings
{
    public static class NoteWindowUiSettings
    {
        public const string AutoCollapseChromeKey = "note.autoCollapseChrome";
        public const bool DefaultAutoCollapseChrome = true;

        public static bool IsAutoCollapseChromeEnabled(SettingsStore settingsStore)
        {
            var raw = settingsStore.GetValue(
                AutoCollapseChromeKey,
                shouldSync: false,
                DefaultAutoCollapseChrome.ToString());

            return !bool.TryParse(raw, out var enabled) || enabled;
        }
    }
}
