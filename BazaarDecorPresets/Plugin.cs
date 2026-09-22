using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using BokuMono;
using HarmonyLib;
using UnityEngine;

namespace BazaarDecorPresets;

[BepInPlugin("com.icy.bazaardecorpresets", "BazaarDecorPresets", Plugin.Version)]
[BepInDependency(TransmogBridge.PluginGuid, BepInDependency.DependencyFlags.SoftDependency)]
public sealed class Plugin : BasePlugin
{
    internal const string Version = "1.0.0";
    internal static ManualLogSource Logger = null!;
    private Harmony harmony;
    public override void Load()
    {
        Logger = Log;
        try
        {
            Localization.Load();
            TransmogBridge.Initialize();
            NativePresetUi.Configure();
            harmony = new Harmony("com.icy.bazaardecorpresets");
            harmony.PatchAll(typeof(Plugin).Assembly);
            AddComponent<NativePresetDriver>();
            Log.LogInfo("BDP Ready " + Version);
        }
        catch (Exception ex)
        {
            NativePresetUi.ReportFault(ex);
            NativePresetUi.CleanupStep("startup-patches", () => harmony?.UnpatchSelf());
        }
    }
}

public sealed class NativePresetDriver : MonoBehaviour
{
    public NativePresetDriver(IntPtr pointer) : base(pointer) { }
    public void Update() => NativePresetUi.Guard(NativePresetUi.Tick);
}

[HarmonyPatch(typeof(BazaarManager), nameof(BazaarManager.OpenBazaarCustomMenu))]
internal static class EditorOpening
{
    static void Prefix(BazaarManager __instance) => NativePresetUi.Guard(() => NativePresetUi.Begin(__instance));
}

[HarmonyPatch(typeof(UIBazaarCustomPage), nameof(UIBazaarCustomPage.PostShow))]
internal static class PageShown
{
    static void Postfix(UIBazaarCustomPage __instance) => NativePresetUi.Guard(() => NativePresetUi.Observe(__instance));
}

[HarmonyPatch(typeof(UIBazaarCustomPage), nameof(UIBazaarCustomPage.ShowPage))]
internal static class PagePreparing
{
    static void Prefix(UIBazaarCustomPage __instance) => NativePresetUi.Guard(() => NativePresetUi.Observe(__instance));
}

[HarmonyPatch(typeof(BazaarManager), nameof(BazaarManager.SaveAndCloseBazaarCustom))]
internal static class EditorSave
{
    static void Prefix() => NativePresetUi.Guard(NativePresetUi.End);
}

[HarmonyPatch(typeof(BazaarManager), nameof(BazaarManager.CloseBazaarCustom))]
internal static class EditorCancel
{
    static void Prefix() => NativePresetUi.Guard(NativePresetUi.End);
}

[HarmonyPatch(typeof(UIMenuFooter), nameof(UIMenuFooter.SortGuide))]
internal static class PresetFooterGuide
{
    static void Postfix(ref Il2CppSystem.Collections.Generic.List<UIButtonGuideData> __result)
    {
        try { Update(__result); }
        catch (Exception ex) { NativePresetUi.ReportFault(ex); }
    }

    private static void Update(Il2CppSystem.Collections.Generic.List<UIButtonGuideData> __result)
    {
        if (__result == null) return;
        // Remove only our entry, never another Mod's Start guide.
        for (int i = __result.Count - 1; i >= 0; i--)
            if (__result[i] != null && __result[i].button == GuideKey.Start &&
                __result[i].textId == UiTextIds.FooterPresets) __result.RemoveAt(i);
        if (NativePresetUi.SessionFaulted || NativePresetUi.AppearanceBlocksPresets) return;
        if (NativePresetUi.IsCompletionNoticeOpen)
        {
            bool hasConfirm = false;
            for (int i = 0; i < __result.Count; i++)
                if (__result[i] != null && __result[i].button == GuideKey.East) hasConfirm = true;
            if (!hasConfirm)
                __result.Insert(0, new UIButtonGuideData(GuideKey.East, UiTextIds.StockConfirmFooter,
                    LocalizeTextTableType.KeyButtonGuideText, InputKeyBind.InputActionSet.Menu));
            return;
        }
        if (NativePresetUi.IsNameInputOpen)
        {
            bool hasCancel = false;
            for (int i = 0; i < __result.Count; i++)
                if (__result[i] != null && __result[i].button == GuideKey.South) hasCancel = true;
            if (!hasCancel)
                __result.Insert(0, new UIButtonGuideData(GuideKey.South, UiTextIds.StockCancelFooter,
                    LocalizeTextTableType.KeyButtonGuideText, InputKeyBind.InputActionSet.Menu));
            return;
        }
        if (NativePresetUi.IsSlotMenuOpen)
        {
            for (int i = __result.Count - 1; i >= 0; i--)
                if (__result[i] != null && __result[i].button == GuideKey.North) __result.RemoveAt(i);
            __result.Insert(0, new UIButtonGuideData(GuideKey.North, UiTextIds.FooterDelete,
                LocalizeTextTableType.KeyButtonGuideText, InputKeyBind.InputActionSet.Menu));
            return;
        }
        if (!NativePresetUi.ShouldShowFooterGuide) return;
        // ShouldShowFooterGuide already requires the active object-editor page,
        // so this cannot affect another menu sharing the footer prefab.
        for (int i = 0; i < __result.Count; i++)
        {
            var guide = __result[i];
            if (guide != null && guide.button == GuideKey.Start && guide.textId == UiTextIds.FooterPresets) return;
        }
        __result.Insert(0, new UIButtonGuideData(GuideKey.Start, UiTextIds.FooterPresets,
            LocalizeTextTableType.KeyButtonGuideText, InputKeyBind.InputActionSet.Menu));
    }
}

[HarmonyPatch(typeof(ControllableUI), nameof(ControllableUI.OnStart))]
internal static class PresetStartInput
{
    static bool Prefix(ControllableUI __instance)
    {
        bool claimed = false;
        try
        {
            if (!NativePresetUi.CanOpenFromStart(__instance)) return true;
            claimed = true;
            NativePresetUi.OpenFromStart();
            return false;
        }
        catch (Exception ex) { NativePresetUi.ReportFault(ex); return !claimed; }
    }
}

[HarmonyPatch(typeof(ControllableUI), nameof(ControllableUI.OnNorth))]
internal static class PresetDeleteInput
{
    static bool Prefix(ControllableUI __instance)
    {
        if (NativePresetUi.SessionFaulted) return true;
        bool owned = false;
        try
        {
            // Already-dispatched Y can outlive the input-enable check.
            owned = NativePresetUi.ShouldBlockEditorInput(__instance);
            if (owned) return false;
            owned = NativePresetUi.OwnsSlotInput(__instance);
            return !owned || !NativePresetUi.TryOpenDeleteForFocusedSlot();
        }
        catch (Exception ex) { NativePresetUi.ReportFault(ex); return !owned; }
    }
}

// Use stock eligibility for navigation and buttons, scoped to the editor tree.
// Icon contents override the base eligibility method independently of the page.
[HarmonyPatch]
internal static class PresetEditorInputEligibility
{
    static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
    {
        yield return AccessTools.DeclaredMethod(typeof(UIBazaarCustomPage), nameof(UIBazaarCustomPage.IsInputEnable));
        yield return AccessTools.DeclaredMethod(typeof(UIIconContent), nameof(UIIconContent.IsInputEnable));
    }

    static void Postfix(ControllableUI __instance, ref bool __result)
    {
        if (!__result) return;
        try { if (NativePresetUi.ShouldBlockEditorInput(__instance)) __result = false; }
        catch (Exception ex) { NativePresetUi.ReportFault(ex); }
    }
}

[HarmonyPatch(typeof(ControllableUI), nameof(ControllableUI.OnSouth))]
internal static class PresetBackgroundCancelGuard
{
    static bool Prefix(ControllableUI __instance)
    {
        bool blocked = false;
        try
        {
            blocked = NativePresetUi.ShouldBlockEditorInput(__instance);
            return !blocked;
        }
        catch (Exception ex)
        {
            NativePresetUi.ReportFault(ex);
            return !blocked;
        }
    }
}

[HarmonyPatch(typeof(ControllableUI), nameof(ControllableUI.OnSouth))]
internal static class PresetNameChoiceCancel
{
    static bool Prefix(ControllableUI __instance)
    {
        bool owned = false;
        try
        {
            var keyboard = NativePresetUi.GetOwnNameChoiceKeyboard(__instance);
            if (keyboard == null) return true;
            owned = true;
            return NativePresetUi.PrepareNameChoiceCancel(keyboard);
        }
        catch (Exception ex)
        {
            NativePresetUi.ReportFault(ex);
            return !owned;
        }
    }
}

[HarmonyPatch(typeof(UIDialog), nameof(UIDialog.OnDecide))]
internal static class PresetEmptyLoadSlotGuard
{
    static bool Prefix(UIDialog __instance, int id)
    {
        if (NativePresetUi.SessionFaulted) return true;
        try { return !NativePresetUi.TryConsumeEmptyLoadChoice(__instance, id); }
        catch (Exception ex) { NativePresetUi.ReportFault(ex); return false; }
    }
}

// BuyPetAnimal normally disables B / Esc. Lift that restriction only while
// the active keyboard request is owned by this Mod.
[HarmonyPatch(typeof(KeyboardManager), "get_IsCancelButtonDisabled")]
internal static class PresetNameInputCancel
{
    static void Postfix(KeyboardManager __instance, ref bool __result)
    {
        try { if (NativePresetUi.IsOwnNameInput(__instance)) __result = false; }
        catch (Exception ex) { NativePresetUi.ReportFault(ex); }
    }
}

[HarmonyPatch(typeof(UIDefaultDialog), nameof(UIDefaultDialog.IsEnabledInputEast))]
internal static class PresetCompletionDialogConfirm
{
    static void Postfix(UIDefaultDialog __instance, ref bool __result)
    {
        try { if (NativePresetUi.OwnsCompletionNotice(__instance)) __result = true; }
        catch (Exception ex) { NativePresetUi.ReportFault(ex); }
    }
}
