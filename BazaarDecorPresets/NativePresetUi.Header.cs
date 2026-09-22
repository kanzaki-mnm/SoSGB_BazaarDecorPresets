using BokuMono;

namespace BazaarDecorPresets;

// The object editor owns its page header.  Keep the original text and only
// present a replacement while a save/load branch of this Mod's flow is active.
internal static partial class NativePresetUi
{
    private static LocalizedTextMeshPro presetTitle;
    private static string originalPresetTitle;

    private static UIPageHeader GetEditorHeader()
    {
        if (page == null || page.myKey != UILoadKey.BazaarCustom) return null;
        var ui = GetUiManager();
        var menu = ui == null ? null : ui.MenuManager;
        var header = menu == null ? null : menu.uiPageHeader;
        // CurrentUIKey changes to the modal's key. The stock header retains
        // the underlying editor key throughout the preset dialog flow.
        return header != null && header.cacheKey == page.myKey ? header : null;
    }

    private static void RefreshPresetTitle()
    {
        if (!open || mode == Mode.None) { RestorePresetTitle(); return; }
        var header = GetEditorHeader();
        var text = header == null ? null : header.headerText;
        if (text == null || !header.gameObject.activeInHierarchy) return;
        string replacement = mode == Mode.Save ? Localization.Get("presets.menu.save") : Localization.Get("presets.menu.load");
        if (presetTitle != text)
        {
            presetTitle = text;
            originalPresetTitle = text.text;
        }
        if (presetTitle != null && presetTitle.text != replacement) presetTitle.text = replacement;
    }

    private static void RestorePresetTitle()
    {
        // A shared header may already belong to another stock screen on exit.
        // Never restore the editor title onto a repurposed header.
        var header = presetTitle == null ? null : GetEditorHeader();
        if (header != null && header.headerText == presetTitle && originalPresetTitle != null && presetTitle.text != originalPresetTitle)
            presetTitle.text = originalPresetTitle;
        presetTitle = null;
        originalPresetTitle = null;
    }
}
