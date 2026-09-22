using BepInEx;
using BokuMono;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace BazaarDecorPresets;

// Preset application changes only the game's unconfirmed editor state. The
// stock editor remains responsible for its final confirm/cancel operation.
internal static partial class NativePresetUi
{
    private const int SlotCount = 6;
    private static BazaarManager editor;
    private static UIBazaarCustomPage page;
    private static UIMenuFooter footer;
    private static UISelectDialog slotDialog;
    private static PresetFile file = new();
    private static string path = "";
    private static string deleteTargetName = "";
    private static int generation, readyFrame, selectedSlot = -1, noDialogFrames, editorGuideId = -1, footerRestoreFrame = -1;
    private static bool storageBlocked, open, slotMenuOpen, nameInputOpen, completionNoticeOpen, returnToSaveSlots, nameFooterRequested, slotFooterRequested, completionFooterRequested, footerRefreshPending, footerRefreshed, footerRestorePending;
    private static Mode mode;
    private static Il2CppSystem.Action<int> choiceCallback, slotCallback, noticeCallback, deleteCallback;
    private static KeyboardManager.InputCompleteCallback nameCallback;
    private static Il2CppSystem.Action nameCancelled;
    private enum Mode { None, Save, Inspect }

    internal static void Configure()
    {
        path = Path.Combine(Paths.ConfigPath, PresetStorage.FileName);
        try
        {
            if (File.Exists(path)) file = PresetStorage.Load(path);
        }
        catch (Exception ex) { storageBlocked = true; Plugin.Logger.LogError("BDP PresetFileError " + ex); }
    }

    internal static void Begin(BazaarManager value)
    {
        generation++;
        editor = value;
        page = null;
        footer = null;
        slotDialog = null;
        footerRefreshPending = footerRefreshed = false;
        footerRestorePending = false;
        editorGuideId = footerRestoreFrame = -1;
        readyFrame = Time.frameCount + 2;
        ClearState();
    }

    internal static void Observe(UIBazaarCustomPage value)
    {
        if (page != value)
        {
            footer = null;
            footerRefreshed = false;
        }
        page = value;
        if (!footerRefreshed) footerRefreshPending = true;
    }

    internal static void End()
    {
        generation++;
        CloseOwnKeyboard();
        ClearState();
        editor = null;
        page = null;
        footer = null;
        slotDialog = null;
        footerRefreshPending = footerRefreshed = false;
        footerRestorePending = false;
        editorGuideId = footerRestoreFrame = -1;
    }

    private static bool Ready => editor != null && editor.IsCustomMode && editor.editCustomData != null && page != null &&
        page.IsActive && page.gameObject.activeInHierarchy && Time.frameCount >= readyFrame;

    // Appearance mode is implemented by another UI layer on the same stock
    // page, so BazaarManager.IsCustomMode alone intentionally remains true.
    // Legacy fallback only; the public API is independent of localization.
    private static bool IsAppearanceEditorVisible()
    {
        if (page == null) return false;
        foreach (var header in page.transform.root.GetComponentsInChildren<UIPageHeader>(true))
        {
            string title = header?.headerText?.text;
            if (title == "みためを変更" || title == "Change Appearance") return true;
        }
        return false;
    }

    internal static bool CanOpenFromStart(ControllableUI source)
    {
        if (!CanEnterPresetMenu || source == null || page?.transform == null || !source.transform.IsChildOf(page.transform)) return false;
        var ui = UnityEngine.Object.FindObjectOfType<UIManager>();
        return ui != null && !ui.IsDialog;
    }

    internal static void OpenFromStart()
    {
        // The input patch already verified the source belongs to our editor.
        // Do not re-test with the page itself: Transform.IsChildOf does not
        // promise that a transform is its own child.
        if (CanEnterPresetMenu) Open();
    }

    internal static bool AppearanceBlocksPresets => TransmogBridge.BlocksPresets(IsAppearanceEditorVisible);
    private static bool CanEnterPresetMenu => Ready && !open && !AppearanceBlocksPresets && page.IsInputEnable();
    internal static bool ShouldShowFooterGuide => Ready && !open && !AppearanceBlocksPresets;

    internal static void Tick()
    {
        if (editor != null && page != null && !editor.IsCustomMode) { End(); return; }
        if (returnToSaveSlots)
        {
            // KeyboardManager invokes its cancel callback before its input UI
            // finishes closing. Reopen the list only after that UI has gone.
            if (Resources.FindObjectsOfTypeAll<UIInputDialog>()
                .Any(dialog => dialog != null && dialog.gameObject.activeInHierarchy)) return;
            returnToSaveSlots = false;
            OpenSlots();
            return;
        }
        var ui = UnityEngine.Object.FindObjectOfType<UIManager>();
        if (nameInputOpen && !nameFooterRequested) RefreshNameInputFooter();
        if (slotMenuOpen && !slotFooterRequested) RefreshSlotListFooter();
        if (completionNoticeOpen && !completionFooterRequested) RefreshCompletionFooter();
        CaptureSlotDialog();
        UpdateObjectPreview();
        RefreshPresetTitle();
        RefreshFooterWhenReady(ui);
        RestoreEditorFooterWhenReady(ui);
        // The stock dialog can be cancelled without calling our ChoicesData
        // callback.  Recover only after two empty frames, so normal close/open
        // transitions keep their own callback chain intact.
        if (open && !nameInputOpen && ui != null && !ui.IsDialog) noDialogFrames++;
        else noDialogFrames = 0;
        if (open && noDialogFrames >= 2)
        {
            Plugin.Logger.LogWarning("BDP RecoveredStaleMenuState");
            FinishMenu();
        }
    }

    private static void RefreshFooterWhenReady(UIManager ui)
    {
        if (!footerRefreshPending || footerRefreshed || !Ready || open || ui == null || ui.IsDialog) return;
        if (footer == null) footer = page.transform.root.GetComponentInChildren<UIMenuFooter>(true);
        if (footer == null || !footer.gameObject.activeInHierarchy || footer.LastGuideId < 0) return;

        // Set the flags before SetGuide: its official rebuild synchronously runs
        // SortGuide, whose postfix adds our Start / + guide.
        footerRefreshPending = false;
        footerRefreshed = true;
        try
        {
            footer.SetGuide((KeyButtonGuideMasterId)(uint)footer.LastGuideId);
        }
        catch (Exception ex)
        {
            // The footer is cosmetic. Keep the editor usable if
            // this game-version-specific refresh path is unavailable.
            Plugin.Logger.LogWarning("BDP FooterRefreshFailed " + ex);
        }
    }

    private static void CaptureEditorFooterGuide()
    {
        if (footer == null && page != null) footer = page.transform.root.GetComponentInChildren<UIMenuFooter>(true);
        if (footer != null && footer.LastGuideId >= 0) editorGuideId = footer.LastGuideId;
    }

    private static void RestoreEditorFooterWhenReady(UIManager ui)
    {
        if (!footerRestorePending || !Ready || open || ui == null || ui.IsDialog || Time.frameCount < footerRestoreFrame) return;
        if (footer == null) footer = page.transform.root.GetComponentInChildren<UIMenuFooter>(true);
        if (footer == null || !footer.gameObject.activeInHierarchy) return;

        footerRestorePending = false;
        try
        {
            footer.SetGuide((KeyButtonGuideMasterId)(uint)editorGuideId);
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning("BDP FooterRestoreFailed " + ex);
        }
    }

    internal static void Guard(Action action)
    {
        try { action(); }
        catch (Exception ex) { Plugin.Logger.LogError("BDP NativeUiError " + ex); Abort(); }
    }

    private static void Open()
    {
        if (!CanEnterPresetMenu) return;
        OpenMainMenu();
    }

    // Reused by the slot-list cancel route.  That route remains inside the
    // same preset session, so it must not reject the existing open=true state.
    private static void ReopenMainMenu() => OpenMainMenu();

    private static void OpenMainMenu()
    {
        var ui = UnityEngine.Object.FindObjectOfType<UIManager>();
        if (!Ready || AppearanceBlocksPresets || (!open && !page.IsInputEnable()) || ui == null || ui.IsDialog) return;
        if (!open) CaptureEditorFooterGuide();
        open = true;
        slotMenuOpen = false;
        slotFooterRequested = false;
        mode = Mode.None;
        int ticket = generation;
        choiceCallback = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<int>>((Action<int>)(choice =>
        { if (ticket == generation) Guard(() => OnMainChoice(choice)); }));
        OpenChoices(ui, new[] { UiTextIds.MenuSave, UiTextIds.MenuInspect, UiTextIds.StockCancel }, choiceCallback);
    }

    private static void OnMainChoice(int choice)
    {
        if (choice == 2) { CloseDialog(FinishMenu); return; }
        if (choice < 0 || choice > 1) return;
        mode = choice == 0 ? Mode.Save : Mode.Inspect;
        RefreshPresetTitle();
        CloseDialog(OpenSlots);
    }

    private static void OpenSlots()
    {
        var ui = UnityEngine.Object.FindObjectOfType<UIManager>();
        if (!Ready || ui == null) { Abort(); return; }
        slotMenuOpen = true;
        slotFooterRequested = false;
        slotDialog = null;
        BeginObjectPreview();
        int ticket = generation;
        slotCallback = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<int>>((Action<int>)(choice =>
        { if (ticket == generation) Guard(() => OnSlotChoice(choice)); }));
        OpenChoices(ui, Enumerable.Range(0, SlotCount).Select(i => UiTextIds.Slot + (uint)i).Append(UiTextIds.StockCancel), slotCallback);
    }

    private static void OnSlotChoice(int choice)
    {
        RemoveObjectPreview();
        slotMenuOpen = false;
        slotFooterRequested = false;
        slotDialog = null;
        if (choice == SlotCount) { CloseDialog(ReopenMainMenu); return; }
        if (choice < 0 || choice >= SlotCount) return;
        selectedSlot = choice + 1;
        if (mode == Mode.Save) CloseDialog(OpenNameInput);
        else CloseDialog(Load);
    }

    private static void OpenNameInput()
    {
        if (storageBlocked) { Notice("presets.storage.blocked", OpenSlots); return; }
        var keyboard = UnityEngine.Object.FindObjectOfType<KeyboardManager>();
        if (!Ready || keyboard == null) { Abort(); return; }
        int ticket = generation;
        nameCallback = DelegateSupport.ConvertDelegate<KeyboardManager.InputCompleteCallback>((Action<KeyboardManager.Result, string>)((result, text) =>
        { if (ticket == generation) Guard(() => SaveName(result, text)); }));
        nameCancelled = DelegateSupport.ConvertDelegate<Il2CppSystem.Action>((Action)(() =>
        { if (ticket == generation) ReturnToSaveSlots(); }));
        nameInputOpen = true;
        nameFooterRequested = false;
        keyboard.ShowRequest(KeyboardManager.KeyboardType.BuyPetAnimal, "", nameCallback, nameCancelled, false, true);
    }

    private static void SaveName(KeyboardManager.Result result, string text)
    {
        nameInputOpen = false;
        nameFooterRequested = false;
        if (result != KeyboardManager.Result.Success) { ReturnToSaveSlots(); return; }
        string name = text?.Trim() ?? "";
        // The stock keyboard constrains this request to eight characters. An
        // empty successful result has no valid preset representation, so leave
        // the save flow without inventing a second validation UI.
        if (name.Length == 0) { returnToSaveSlots = true; return; }
        var candidate = file.Copy();
        PresetStorage.Put(candidate, selectedSlot, new Preset { Name = name, Slots = Snapshot() });
        try
        {
            PresetStorage.Save(path, candidate);
            file = candidate;
            Notice("presets.save.completed", FinishMenu);
        }
        catch (Exception ex) { Plugin.Logger.LogError("BDP PresetSaveError " + ex); Notice("save.failed", OpenSlots); }
    }

    private static void Load()
    {
        var preset = PresetStorage.At(file, selectedSlot);
        if (preset == null) OpenSlots();
        else
        {
            // Apply owns a snapshot taken at this exact point and restores it
            // on any mutation failure. It is intentionally not the layout at
            // the start of the editor session.
            var beforeLoad = Snapshot();
            try
            {
                var plan = PresetApplicator.Plan(page, preset, beforeLoad);
                PresetApplicator.Apply(editor, beforeLoad, plan, Snapshot);
                Notice("presets.load.completed", FinishMenu);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError("BDP PresetLoadError " + ex);
                Notice("presets.load.failed", OpenSlots);
            }
        }
    }

    private static void OpenDeleteConfirmation()
    {
        if (storageBlocked) { Notice("presets.storage.blocked", OpenSlots); return; }
        var preset = PresetStorage.At(file, selectedSlot);
        if (preset == null) { OpenSlots(); return; }
        var ui = UnityEngine.Object.FindObjectOfType<UIManager>();
        if (ui == null) { Abort(); return; }
        deleteTargetName = preset.Name;
        int ticket = generation;
        deleteCallback = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<int>>((Action<int>)(choice =>
        { if (ticket == generation) Guard(() => OnDeleteChoice(choice)); }));
        var ids = new Il2CppSystem.Collections.Generic.List<uint>();
        ids.Add(UiTextIds.StockYes);
        ids.Add(UiTextIds.StockCancel);
        var choices = new ChoicesData(ids, deleteCallback, LocalizeTextTableType.DialogChoiceText);
        var data = new UIDefaultDialogData(UiTextIds.DeleteConfirm, choices, new Il2CppStringArray(0L));
        ui.OpenDialog(UILoadKey.DefaultDialog, data, DialogMaskType.Translucent, UIDialogManager.AnchorType.Center, false, true, null, null);
    }

    private static void OnDeleteChoice(int choice)
    {
        if (choice != 0) { CloseDialog(OpenSlots); return; }
        var candidate = file.Copy();
        PresetStorage.Remove(candidate, selectedSlot);
        try
        {
            PresetStorage.Save(path, candidate);
            file = candidate;
            CloseDialog(() => Notice("presets.delete.completed", OpenSlots));
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError("BDP PresetDeleteError " + ex);
            CloseDialog(() => Notice("save.failed", OpenSlots));
        }
    }

    private static List<Slot> Snapshot()
    {
        if (!Ready) throw new InvalidOperationException("Editor is not ready.");
        var result = new List<Slot>();
        var groups = editor.editCustomData.PutPartsDataDic ?? throw new InvalidOperationException("Layout is unavailable.");
        for (int g = 0; g < groups.Count; g++)
        {
            var group = groups[g];
            if (group?.DataDic == null) throw new InvalidOperationException("Layout group is unavailable.");
            for (int i = 0; i < group.DataDic.Count; i++) result.Add(new Slot(group.Category.ToString(), i, group.DataDic[i]));
        }
        PresetStorage.ValidateSlots(result);
        return result;
    }

    private static void Notice(string key, Action after)
    {
        completionNoticeOpen = key == "presets.save.completed" || key == "presets.load.completed" ||
            key == "presets.delete.completed";
        completionFooterRequested = false;
        NoticeMessage(Localization.Get(key), after);
    }

    private static void NoticeMessage(string message, Action after)
    {
        var ui = UnityEngine.Object.FindObjectOfType<UIManager>();
        if (ui == null) { after?.Invoke(); return; }
        NoticeText.Current = message;
        int ticket = generation;
        noticeCallback = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<int>>((Action<int>)(_ =>
        {
            if (ticket != generation) return;
            completionNoticeOpen = false;
            completionFooterRequested = false;
            CloseDialog(after);
        }));
        var choices = new ChoicesData(new Il2CppSystem.Collections.Generic.List<uint>(), noticeCallback, LocalizeTextTableType.DialogChoiceText);
        var data = new UIDefaultDialogData(UiTextIds.Notice, choices, new Il2CppStringArray(0L));
        ui.OpenDialog(UILoadKey.MessageDialogSmall, data, DialogMaskType.Translucent, UIDialogManager.AnchorType.Center, false, true, null, null);
    }

    private static void OpenChoices(UIManager ui, IEnumerable<uint> ids, Il2CppSystem.Action<int> callback)
    {
        var textIds = new Il2CppSystem.Collections.Generic.List<uint>();
        foreach (var id in ids) textIds.Add(id);
        var choices = new ChoicesData(textIds, callback, LocalizeTextTableType.DialogChoiceText);
        ui.OpenDialog(UILoadKey.SelectDialog, new UIDialogData(choices), DialogMaskType.Translucent, UIDialogManager.AnchorType.Center, false, true, null, null);
    }

    private static void CloseDialog(Action after)
    {
        var ui = UnityEngine.Object.FindObjectOfType<UIManager>();
        if (ui == null) { after?.Invoke(); return; }
        int ticket = generation;
        var callback = DelegateSupport.ConvertDelegate<Il2CppSystem.Action>((Action)(() => { if (ticket == generation) after?.Invoke(); }));
        ui.CloseDialog(null, callback, true, true, false);
    }

    private static void CloseOwnKeyboard()
    {
        var keyboard = UnityEngine.Object.FindObjectOfType<KeyboardManager>();
        if (!nameInputOpen || keyboard == null || nameCallback == null) return;
        if (keyboard.completeCallback != null && IL2CPP.Il2CppObjectBaseToPtr(keyboard.completeCallback) == IL2CPP.Il2CppObjectBaseToPtr(nameCallback)) keyboard.SetResultCancel();
    }

    private static void ClearState()
    {
        RestorePresetTitle();
        open = slotMenuOpen = nameInputOpen = completionNoticeOpen = returnToSaveSlots = nameFooterRequested = slotFooterRequested = completionFooterRequested = false;
        noDialogFrames = 0;
        selectedSlot = -1;
        deleteTargetName = "";
        mode = Mode.None;
        choiceCallback = slotCallback = noticeCallback = deleteCallback = null;
        nameCallback = null;
        nameCancelled = null;
        slotDialog = null;
        RemoveObjectPreview();
    }

    private static void ReturnToSaveSlots()
    {
        if (!nameInputOpen) return;
        nameInputOpen = false;
        nameFooterRequested = false;
        returnToSaveSlots = true;
    }

    private static void RefreshNameInputFooter()
    {
        foreach (var candidate in Resources.FindObjectsOfTypeAll<UIMenuFooter>())
        {
            if (candidate == null || !candidate.gameObject.activeInHierarchy ||
                candidate.guideTarget == null || candidate.LastGuideId < 0) continue;
            if (!candidate.guideTarget.GetComponentsInChildren<UIButtonGuide>(true)
                .Any(guide => guide != null && guide.gameObject.activeInHierarchy && guide.button == GuideKey.East)) continue;
            nameFooterRequested = true;
            candidate.SetGuide((KeyButtonGuideMasterId)(uint)candidate.LastGuideId);
            return;
        }
    }

    private static void RefreshSlotListFooter()
    {
        foreach (var candidate in Resources.FindObjectsOfTypeAll<UIMenuFooter>())
        {
            if (candidate == null || !candidate.gameObject.activeInHierarchy ||
                candidate.guideTarget == null || candidate.LastGuideId < 0) continue;
            if (!candidate.guideTarget.GetComponentsInChildren<UIButtonGuide>(true)
                .Any(guide => guide != null && guide.gameObject.activeInHierarchy && guide.button == GuideKey.East)) continue;
            slotFooterRequested = true;
            candidate.SetGuide((KeyButtonGuideMasterId)(uint)candidate.LastGuideId);
            return;
        }
    }

    private static void RefreshCompletionFooter()
    {
        foreach (var candidate in Resources.FindObjectsOfTypeAll<UIMenuFooter>())
        {
            if (candidate == null || !candidate.gameObject.activeInHierarchy ||
                candidate.guideTarget == null || candidate.LastGuideId < 0) continue;
            if (!candidate.guideTarget.GetComponentsInChildren<UIButtonGuide>(true)
                .Any(guide => guide != null && guide.gameObject.activeInHierarchy && guide.button == GuideKey.South)) continue;
            completionFooterRequested = true;
            candidate.SetGuide((KeyButtonGuideMasterId)(uint)candidate.LastGuideId);
            return;
        }
    }

    private static void CaptureSlotDialog()
    {
        if (!slotMenuOpen || (slotDialog != null && slotDialog.gameObject.activeInHierarchy)) return;
        slotDialog = Resources.FindObjectsOfTypeAll<UISelectDialog>().LastOrDefault(candidate =>
            candidate != null && candidate.gameObject.activeInHierarchy &&
            candidate.GetComponentsInChildren<UIDialogChoiceBar>(true).Any(bar =>
                bar != null && bar.cacheData?.TextId >= UiTextIds.Slot &&
                bar.cacheData.TextId < UiTextIds.Slot + SlotCount));
    }
    private static void FinishMenu()
    {
        ClearState();
        // The dialog footer overwrites LastGuideId with its B/A layout.  Resume
        // the editor's ID captured before opening only after the close animation.
        if (editorGuideId >= 0)
        {
            footerRestorePending = true;
            footerRestoreFrame = Time.frameCount + 1;
        }
    }
    private static void Abort() { CloseOwnKeyboard(); ClearState(); }

    internal static bool TryGetText(LanguageManager language, LocalizeTextTableType table, uint id, out string text)
    {
        Localization.SetLanguage(language.CurrentLanguage);
        if (id == UiTextIds.Notice) { text = NoticeText.Current; return true; }
        if (id == UiTextIds.DeleteConfirm) { text = Localization.Get("presets.delete.confirm").Replace("{0}", deleteTargetName); return true; }
        if (id == UiTextIds.FooterPresets) { text = Localization.Get("presets.guide"); return true; }
        if (id == UiTextIds.FooterDelete) { text = Localization.Get("presets.delete.guide"); return true; }
        if (table == LocalizeTextTableType.DialogChoiceText)
        {
            if (id == UiTextIds.MenuSave) { text = Localization.Get("presets.menu.save"); return true; }
            if (id == UiTextIds.MenuInspect) { text = Localization.Get("presets.menu.load"); return true; }
            if (id >= UiTextIds.Slot && id < UiTextIds.Slot + SlotCount)
            {
                var preset = PresetStorage.At(file, (int)(id - UiTextIds.Slot) + 1);
                text = preset == null ? Localization.Get("presets.slots.empty") : Localization.Slot((int)(id - UiTextIds.Slot) + 1, preset.Name);
                return true;
            }
        }
        if (id == UiTextIds.NameInputTextId && nameInputOpen) { text = Localization.Get("presets.name.prompt"); return true; }
        if (id == UiTextIds.NameConfirmTextId && nameInputOpen)
        {
            var keyboard = UnityEngine.Object.FindObjectOfType<KeyboardManager>();
            text = Localization.Get("presets.name.confirm").Replace("{0}", keyboard?.inputedText ?? "");
            return true;
        }
        text = null;
        return false;
    }

    internal static bool IsOwnNameInput(KeyboardManager keyboard)
    {
        if (!nameInputOpen || keyboard == null || nameCallback == null) return false;
        var callback = keyboard.completeCallback;
        return callback != null && IL2CPP.Il2CppObjectBaseToPtr(callback) == IL2CPP.Il2CppObjectBaseToPtr(nameCallback);
    }

    internal static bool IsNameInputOpen => nameInputOpen;
    internal static bool IsCompletionNoticeOpen => completionNoticeOpen;
    internal static bool OwnsCompletionNotice(UIDefaultDialog dialog) => completionNoticeOpen && dialog != null && dialog.infoId == UiTextIds.Notice;
    internal static bool IsSlotMenuOpen => slotMenuOpen;

    internal static bool TryOpenDeleteForFocusedSlot()
    {
        if (!slotMenuOpen) return false;
        // Keep the editor modal isolated even if its preset storage became
        // unavailable after the list opened.
        if (storageBlocked) return true;
        var dialog = Resources.FindObjectsOfTypeAll<UISelectDialog>().LastOrDefault(item =>
            item != null && item.gameObject.activeInHierarchy &&
            item.GetComponentsInChildren<UIDialogChoiceBar>(true).Any(bar =>
                bar != null && bar.cacheData?.TextId >= UiTextIds.Slot &&
                bar.cacheData.TextId < UiTextIds.Slot + SlotCount));
        if (dialog == null) return false;
        var focused = dialog.GetComponentsInChildren<UIDialogChoiceBar>(true)
            .FirstOrDefault(bar => bar != null && bar.gameObject.activeInHierarchy && bar.IsFocused);
        int index = focused?.data?.id ?? -1;
        // This is our list, so consume Y even on Cancel / an empty slot.
        if (index < 0 || index >= SlotCount || PresetStorage.At(file, index + 1) == null) return true;
        selectedSlot = index + 1;
        slotMenuOpen = false;
        slotFooterRequested = false;
        RemoveObjectPreview();
        CloseDialog(OpenDeleteConfirmation);
        return true;
    }

    // UIDialog completes its ChoicesData before it invokes the callback.  An
    // empty load slot is intentionally inert, so consume its A decision while
    // focus and the normal select feedback remain on the list.
    internal static bool TryConsumeEmptyLoadChoice(UIDialog dialog, int index)
    {
        if (mode != Mode.Inspect || !slotMenuOpen || dialog == null || slotDialog == null ||
            index < 0 || index >= SlotCount) return false;
        if (IL2CPP.Il2CppObjectBaseToPtr(dialog) != IL2CPP.Il2CppObjectBaseToPtr(slotDialog)) return false;
        return PresetStorage.At(file, index + 1) == null;
    }
}

internal static class NoticeText { internal static string Current = ""; }

[HarmonyPatch(typeof(LanguageManager), nameof(LanguageManager.GetLocalizeText), new[] { typeof(LocalizeTextTableType), typeof(uint), typeof(bool) })]
internal static class NativePresetLocalization
{
    static bool Prefix(LanguageManager __instance, LocalizeTextTableType tableType, uint textId, ref string __result)
    {
        if (!NativePresetUi.TryGetText(__instance, tableType, textId, out var text)) return true;
        __result = text;
        return false;
    }
}
