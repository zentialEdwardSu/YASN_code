namespace YASN.Settings
{
    public static class EditorDisplayModeSettings
    {
        public const string SettingKey = "editor.enterMode";
        public const string TextOnlyValue = "textOnly";
        public const string TextAndPreviewValue = "textAndPreview";
        public const string PreviewOnlyValue = "previewOnly";
        public const string DefaultValue = TextAndPreviewValue;

        public static EditorDisplayMode ParseValue(string? value)
        {
            return value?.Trim() switch
            {
                TextOnlyValue => EditorDisplayMode.TextOnly,
                PreviewOnlyValue => EditorDisplayMode.PreviewOnly,
                _ => EditorDisplayMode.TextAndPreview
            };
        }

        public static bool TryParseValue(string? value, out EditorDisplayMode mode)
        {
            mode = EditorDisplayMode.TextAndPreview;
            switch (value?.Trim())
            {
                case TextOnlyValue:
                    mode = EditorDisplayMode.TextOnly;
                    return true;
                case TextAndPreviewValue:
                    mode = EditorDisplayMode.TextAndPreview;
                    return true;
                case PreviewOnlyValue:
                    mode = EditorDisplayMode.PreviewOnly;
                    return true;
                default:
                    return false;
            }
        }

        public static string ToValue(EditorDisplayMode mode)
        {
            return mode switch
            {
                EditorDisplayMode.TextOnly => TextOnlyValue,
                EditorDisplayMode.PreviewOnly => PreviewOnlyValue,
                _ => TextAndPreviewValue
            };
        }

        public static EditorDisplayMode GetEnterMode(SettingsStore settingsStore)
        {
            var raw = settingsStore.GetValue(
                SettingKey,
                shouldSync: false,
                defaultValue: DefaultValue);
            return ParseValue(raw);
        }
    }
}
