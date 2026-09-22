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
    private static UIManager uiManager;
    private static bool sessionActive;
    private static bool loadRecoveryBlocked;
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
    private static KeyboardManager pendingNameChoiceCancel;
    private static readonly UiCallbackGate uiCallbacks = new();
    private static long nameRequestTicket;
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
        uiCallbacks.Invalidate();
        SessionFaulted = false;
        loadRecoveryBlocked = false;
        faultFooterRestored = false;
        dialogCloseTracker.Reset();
        editor = value;
        sessionActive = true;
        uiManager = null;
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
        uiCallbacks.Invalidate();
        Guard(CloseOwnKeyboard);
        Guard(ClearState);
        ResetMenuState();
        editor = null;
        sessionActive = false;
        dialogCloseTracker.Reset();
        uiManager = null;
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
        var ui = GetUiManager();
        return ui != null && !ui.IsDialog;
    }

    internal static void OpenFromStart()
    {
        // The input patch already verified the source belongs to our editor.
        // Do not re-test with the page itself: Transform.IsChildOf does not
        // promise that a transform is its own child.
        if (CanEnterPresetMenu) Open();
    }

    internal static bool ShouldBlockEditorInput(ControllableUI source)
    {
        // Stock keyboard transitions briefly expose the editor between dialogs.
        // The preset flow still owns input during that gap, even if IsDialog is
        // false. Do not gate this on Ready or the keyboard's visible state.
        if (!sessionActive || !open || page == null || source == null ||
            page.myKey != UILoadKey.BazaarCustom) return false;
        var target = source.transform;
        var editorRoot = page.transform;
        if (target == null || editorRoot == null ||
            (target != editorRoot && !target.IsChildOf(editorRoot))) return false;
        // Preserve input on the foreground dialogs, including any that
        // a future game version parents under the editor itself.
        return source.GetComponentInParent<UIDialog>() == null;
    }

    internal static bool AppearanceBlocksPresets => TransmogBridge.BlocksPresets(IsAppearanceEditorVisible);
    private static bool CanEnterPresetMenu => !SessionFaulted && !loadRecoveryBlocked && Ready && !open && !AppearanceBlocksPresets && page.IsInputEnable();
    internal static bool ShouldShowFooterGuide => !SessionFaulted && !loadRecoveryBlocked && Ready && !open && !AppearanceBlocksPresets;

    internal static void Tick()
    {
        if (!sessionActive) return;
        // Unity destruction must release our UI before the idle fast path.
        if (editor == null || (page != null && !editor.IsCustomMode)) { End(); return; }
        if (SessionFaulted) { TickFaultCleanup(); return; }
        CompleteNameChoiceCancel();
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
        var ui = GetUiManager();
        if (nameInputOpen && !nameFooterRequested) RefreshDialogFooter(GuideKey.East, ref nameFooterRequested);
        if (slotMenuOpen && !slotFooterRequested) RefreshDialogFooter(GuideKey.East, ref slotFooterRequested);
        if (completionNoticeOpen && !completionFooterRequested) RefreshDialogFooter(GuideKey.South, ref completionFooterRequested);
        CaptureSlotDialog();
        UpdateObjectPreview();
        RefreshPresetTitle();
        RefreshFooterWhenReady(ui);
        RestoreEditorFooterWhenReady(ui);
        // The stock dialog can be cancelled without calling our ChoicesData
        // callback.  Recover only after two empty frames, so normal close/open
        // transitions keep their own callback chain intact.
        if (open && !nameInputOpen && !dialogCloseTracker.IsClosing && ui != null && !ui.IsDialog) noDialogFrames++;
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
        catch (Exception ex) { ReportFault(ex); }
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
        var ui = GetUiManager();
        if (SessionFaulted || loadRecoveryBlocked || !Ready || AppearanceBlocksPresets || (!open && !page.IsInputEnable()) || ui == null || ui.IsDialog) return;
        if (!open) CaptureEditorFooterGuide();
        open = true;
        slotMenuOpen = false;
        slotFooterRequested = false;
        mode = Mode.None;
        int ticket = generation;
        long request = uiCallbacks.Begin();
        choiceCallback = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<int>>((Action<int>)(choice =>
        { if (ticket == generation && uiCallbacks.TryConsume(request)) Guard(() => OnMainChoice(choice)); }));
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
        var ui = GetUiManager();
        if (!Ready || ui == null) { Abort(); return; }
        slotMenuOpen = true;
        slotFooterRequested = false;
        slotDialog = null;
        BeginObjectPreview();
        int ticket = generation;
        long request = uiCallbacks.Begin();
        slotCallback = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<int>>((Action<int>)(choice =>
        { if (ticket == generation && uiCallbacks.TryConsume(request)) Guard(() => OnSlotChoice(choice)); }));
        OpenChoices(ui, Enumerable.Range(0, SlotCount).Select(i => UiTextIds.Slot + (uint)i).Append(UiTextIds.StockCancel), slotCallback);
    }

    private static void OnSlotChoice(int choice)
    {
        RemoveObjectPreview(false);
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
        int targetSlot = selectedSlot;
        long request = nameRequestTicket = uiCallbacks.Begin();
        nameCallback = DelegateSupport.ConvertDelegate<KeyboardManager.InputCompleteCallback>((Action<KeyboardManager.Result, string>)((result, text) =>
        { if (ticket == generation && uiCallbacks.TryConsume(request)) Guard(() => SaveName(result, text, targetSlot)); }));
        nameCancelled = DelegateSupport.ConvertDelegate<Il2CppSystem.Action>((Action)(() =>
        { if (ticket == generation && uiCallbacks.TryConsume(request)) Guard(ReturnToSaveSlots); }));
        nameInputOpen = true;
        nameFooterRequested = false;
        keyboard.ShowRequest(KeyboardManager.KeyboardType.BuyPetAnimal, "", nameCallback, nameCancelled, false, true);
    }

    private static void SaveName(KeyboardManager.Result result, string text, int targetSlot)
    {
        pendingNameChoiceCancel = null;
        if (!NameInputCompletion.Complete(result == KeyboardManager.Result.Success,
            ref nameInputOpen, ref nameFooterRequested, ref returnToSaveSlots)) return;
        string name = text?.Trim() ?? "";
        // The stock keyboard constrains this request to eight characters. An
        // empty successful result has no valid preset representation, so leave
        // the save flow without inventing a second validation UI.
        if (name.Length == 0) { returnToSaveSlots = true; return; }
        var candidate = file.Copy();
        PresetStorage.Put(candidate, targetSlot, new Preset { Name = name, Slots = Snapshot() });
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
        if (preset == null) { OpenSlots(); return; }
        List<Slot> beforeLoad, plan;
        try
        {
            beforeLoad = Snapshot();
            plan = PresetApplicator.Plan(page, preset, beforeLoad);
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError("BDP PresetValidationError " + ex);
            Notice("presets.load.rejected", OpenSlots);
            return;
        }

        var result = PresetApplicator.Apply(editor, beforeLoad, plan, Snapshot);
        if (result.Status == LayoutApplyStatus.RecoveryUnconfirmed)
        {
            // Keep the warning dialog operational; SessionFaulted would close it.
            // The official editor still owns the user's final confirm/cancel.
            loadRecoveryBlocked = true;
        }
        foreach (var error in result.Errors)
            Plugin.Logger.LogError("BDP PresetApplyError " + error);
        if (loadRecoveryBlocked)
        {
            Notice("presets.load.recovery.failed", FinishMenu);
            return;
        }

        bool modelsUpdated;
        try { modelsUpdated = PresetApplicator.RefreshChangedModels(result.ModelsToRefresh); }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning("BDP PresetModelRefreshFailed " + ex);
            modelsUpdated = false;
        }
        if (result.Status == LayoutApplyStatus.Restored)
            Notice(modelsUpdated ? "presets.load.failed" : "presets.load.restored.visual.failed", OpenSlots);
        else
            Notice(modelsUpdated ? "presets.load.completed" : "presets.load.visual.failed", FinishMenu);
    }
    private static void OpenDeleteConfirmation()
    {
        if (storageBlocked) { Notice("presets.storage.blocked", OpenSlots); return; }
        var preset = PresetStorage.At(file, selectedSlot);
        if (preset == null) { OpenSlots(); return; }
        var ui = GetUiManager();
        if (ui == null) { Abort(); return; }
        deleteTargetName = preset.Name;
        int ticket = generation;
        int targetSlot = selectedSlot;
        long request = uiCallbacks.Begin();
        deleteCallback = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<int>>((Action<int>)(choice =>
        { if (ticket == generation && uiCallbacks.TryConsume(request)) Guard(() => OnDeleteChoice(choice, targetSlot)); }));
        var ids = new Il2CppSystem.Collections.Generic.List<uint>();
        ids.Add(UiTextIds.StockYes);
        ids.Add(UiTextIds.StockCancel);
        var choices = new ChoicesData(ids, deleteCallback, LocalizeTextTableType.DialogChoiceText);
        var data = new UIDefaultDialogData(UiTextIds.DeleteConfirm, choices, new Il2CppStringArray(0L));
        ui.OpenDialog(UILoadKey.DefaultDialog, data, DialogMaskType.Translucent, UIDialogManager.AnchorType.Center, false, true, null, null);
    }

    private static void OnDeleteChoice(int choice, int targetSlot)
    {
        if (choice != 0) { CloseDialog(OpenSlots); return; }
        var candidate = file.Copy();
        PresetStorage.Remove(candidate, targetSlot);
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
        var ui = GetUiManager();
        if (ui == null) { after?.Invoke(); return; }
        NoticeText.Current = message;
        int ticket = generation;
        long request = uiCallbacks.Begin();
        noticeCallback = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<int>>((Action<int>)(_ =>
        {
            if (ticket != generation || !uiCallbacks.TryConsume(request)) return;
            Guard(() =>
            {
                completionNoticeOpen = false;
                completionFooterRequested = false;
                CloseDialog(after);
            });
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
        var ui = GetUiManager();
        if (ui == null) { uiCallbacks.Invalidate(); RestoreClosedSlotDialogPosition(); after?.Invoke(); return; }
        int ticket = generation;
        if (!dialogCloseTracker.TryPrepare(true, closeTicket =>
        {
            // Retire choice callbacks even when Y, rather than a choice, closed the list.
            long request = uiCallbacks.Begin();
            return DelegateSupport.ConvertDelegate<Il2CppSystem.Action>((Action)(() =>
            {
                // Stock closing must finish even when navigation has been retired.
                if (!dialogCloseTracker.Complete(closeTicket)) return;
                Guard(() =>
                {
                    // Restore before navigation can reuse the pooled select dialog.
                    RestoreClosedSlotDialogPosition();
                    if (ticket == generation && uiCallbacks.TryConsume(request)) after?.Invoke();
                });
            }));
        }, out var callback)) return;
        // After submission, a thrown API call may already have started closing.
        // Only the completion callback or session reset may release that wait.
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
        uiCallbacks.Invalidate();
        RestorePresetTitle();
        ResetMenuState();
        RemoveObjectPreview();
    }

    private static void ResetMenuState()
    {
        uiCallbacks.Invalidate();
        open = slotMenuOpen = nameInputOpen = completionNoticeOpen = returnToSaveSlots = nameFooterRequested = slotFooterRequested = completionFooterRequested = false;
        noDialogFrames = 0;
        selectedSlot = -1;
        deleteTargetName = "";
        mode = Mode.None;
        choiceCallback = slotCallback = noticeCallback = deleteCallback = null;
        nameCallback = null;
        pendingNameChoiceCancel = null;
        nameCancelled = null;
        slotDialog = null;
    }

    private static void ReturnToSaveSlots()
    {
        pendingNameChoiceCancel = null;
        NameInputCompletion.Complete(false, ref nameInputOpen, ref nameFooterRequested, ref returnToSaveSlots);
    }

    private static UIManager GetUiManager()
    {
        // Unity's null check also detects a destroyed native object after a scene change.
        if (uiManager == null || !uiManager.gameObject.activeInHierarchy)
            uiManager = UnityEngine.Object.FindObjectOfType<UIManager>();
        return uiManager;
    }

    private static void RefreshDialogFooter(GuideKey button, ref bool requested)
    {
        foreach (var candidate in Resources.FindObjectsOfTypeAll<UIMenuFooter>())
        {
            if (candidate == null || !candidate.gameObject.activeInHierarchy ||
                candidate.guideTarget == null || candidate.LastGuideId < 0) continue;
            if (!candidate.guideTarget.GetComponentsInChildren<UIButtonGuide>(true)
                .Any(guide => guide != null && guide.gameObject.activeInHierarchy && guide.button == button)) continue;
            // SetGuide synchronously invokes our footer patch, so mark first.
            requested = true;
            candidate.SetGuide((KeyButtonGuideMasterId)(uint)candidate.LastGuideId);
            return;
        }
    }

    private static int FocusedSlotIndex()
    {
        // Do not retain the row array: the stock dialog owns its reconstruction.
        var focused = slotDialog.GetComponentsInChildren<UIDialogChoiceBar>(true)
            .FirstOrDefault(bar => bar != null && bar.gameObject.activeInHierarchy && bar.IsFocused);
        return focused?.data?.id ?? -1;
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
    private static void Abort() { uiCallbacks.Invalidate(); CloseOwnKeyboard(); ClearState(); }

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
                text = Localization.Slot((int)(id - UiTextIds.Slot) + 1,
                    preset == null ? Localization.Get("presets.slots.empty") : preset.Name);
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

    internal static KeyboardManager GetOwnNameChoiceKeyboard(ControllableUI source)
    {
        if (!nameInputOpen || source == null || source.TryCast<UIDialogChoiceBar>() == null) return null;
        var input = source.GetComponentInParent<UIInputDialog>();
        if (input == null || !input.gameObject.activeInHierarchy) return null;
        var keyboard = UnityEngine.Object.FindObjectOfType<KeyboardManager>();
        return IsOwnNameInput(keyboard) ? keyboard : null;
    }

    internal static bool PrepareNameChoiceCancel(KeyboardManager keyboard)
    {
        // Unlike the text-field B handler, the stock decision-row B handler
        // can close Input without supplying a result to KeyboardManager.
        // Keep its close/SE behavior, but supply the same result as the field.
        // Once submission has started, another B must not close the UI again.
        if (keyboard.stateContext == null || keyboard.stateContext.CurrentState != KeyboardManager.State.Inputting ||
            keyboard.result != KeyboardManager.Result.None)
        {
            return false;
        }
        pendingNameChoiceCancel = keyboard;
        keyboard.SetResultCancel();
        return true;
    }

    private static void CompleteNameChoiceCancel()
    {
        // The decision-row close can finish the stock keyboard without its
        // cancel callback. Only bridge a cancel we explicitly requested, and
        // wait for actual completion rather than guessing an animation delay.
        if (!nameInputOpen || pendingNameChoiceCancel == null) return;
        var context = pendingNameChoiceCancel.stateContext;
        if (context == null || context.CurrentState != KeyboardManager.State.Idle) return;
        var ui = GetUiManager();
        if (ui == null || ui.IsDialog) return;
        if (uiCallbacks.TryConsume(nameRequestTicket)) ReturnToSaveSlots();
    }

    internal static bool IsNameInputOpen => nameInputOpen;
    internal static bool IsCompletionNoticeOpen => completionNoticeOpen;
    internal static bool OwnsCompletionNotice(UIDefaultDialog dialog) => completionNoticeOpen && dialog != null && dialog.infoId == UiTextIds.Notice;
    internal static bool IsSlotMenuOpen => slotMenuOpen;

    internal static bool OwnsSlotInput(ControllableUI source)
    {
        if (!slotMenuOpen || source == null || !source.gameObject.activeInHierarchy) return false;
        CaptureSlotDialog();
        if (slotDialog == null || !slotDialog.gameObject.activeInHierarchy) return false;
        // A visible list may be behind another modal. Both the source's nearest
        // dialog and the official foreground page must be this exact instance.
        var owner = source.GetComponentInParent<UIDialog>();
        if (owner == null || IL2CPP.Il2CppObjectBaseToPtr(owner) != IL2CPP.Il2CppObjectBaseToPtr(slotDialog)) return false;
        var ui = GetUiManager();
        return ui != null && ui.IsDialog && ui.CurrentUIKey == UILoadKey.SelectDialog &&
            ui.DialogManager != null && ui.DialogManager.TryGetPage(ui.CurrentUIKey, out var current) &&
            current != null && IL2CPP.Il2CppObjectBaseToPtr(current) == IL2CPP.Il2CppObjectBaseToPtr(slotDialog);
    }

    internal static bool TryOpenDeleteForFocusedSlot()
    {
        if (!slotMenuOpen) return false;
        // Keep the editor modal isolated even if its preset storage became
        // unavailable after the list opened.
        if (storageBlocked) return true;
        CaptureSlotDialog();
        if (slotDialog == null || !slotDialog.gameObject.activeInHierarchy) return false;
        int index = FocusedSlotIndex();
        // This is our list, so consume Y even on Cancel / an empty slot.
        if (index < 0 || index >= SlotCount || PresetStorage.At(file, index + 1) == null) return true;
        selectedSlot = index + 1;
        slotMenuOpen = false;
        slotFooterRequested = false;
        RemoveObjectPreview(false);
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
        try
        {
            if (!NativePresetUi.TryGetText(__instance, tableType, textId, out var text)) return true;
            __result = text;
            return false;
        }
        catch (Exception ex)
        {
            NativePresetUi.ReportFault(ex);
            // Never send our synthetic IDs into the stock text database.
            if (textId >= UiTextIds.MenuSave && textId <= UiTextIds.DeleteConfirm)
            {
                __result = "Presets";
                return false;
            }
            return true;
        }
    }
}
