using BokuMono;

namespace BazaarDecorPresets;

// The object editor owns its page header.  Keep the original text and only
// present a replacement while a save/load branch of this Mod's flow is active.
internal static partial class NativePresetUi
{
    private static LocalizedTextMeshPro presetTitle;
    private static string originalPresetTitle;

    private static void RefreshPresetTitle()
    {
        if (!open || mode == Mode.None) { RestorePresetTitle(); return; }
        string replacement = mode == Mode.Save ? Localization.Get("presets.menu.save") : Localization.Get("presets.menu.load");
        if (presetTitle == null && page != null)
        {
            foreach (var header in page.transform.root.GetComponentsInChildren<UIPageHeader>(true))
            {
                var text = header?.headerText;
                if (text == null) continue;
                if (text.text != "オブジェの変更" && text.text != "Redecorate") continue;
                presetTitle = text;
                originalPresetTitle = text.text;
                if (originalPresetTitle == "オブジェの変更") Localization.SetLanguage(Language.ja);
                else Localization.SetLanguage(Language.en);
                break;
            }
        }
        if (presetTitle != null) presetTitle.text = replacement;
    }

    private static void RestorePresetTitle()
    {
        if (presetTitle != null && originalPresetTitle != null) presetTitle.text = originalPresetTitle;
        presetTitle = null;
        originalPresetTitle = null;
    }
}
