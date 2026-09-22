using BokuMono;
using UnityEngine;

namespace BazaarDecorPresets;

internal static partial class NativePresetUi
{
    internal static bool SessionFaulted { get; private set; }
    private static readonly DialogCloseTracker dialogCloseTracker = new();
    private static bool faultFooterRestored, faultCleanupFailed;

    internal static void ReportFault(Exception error)
    {
        // Close callbacks can run synchronously. Invalidate them before touching UI.
        if (SessionFaulted) return;
        SessionFaulted = true;
        faultFooterRestored = faultCleanupFailed = false;
        generation++;
        uiCallbacks.Invalidate();
        CleanupStep("log", () => Plugin.Logger.LogError("BDP SessionDisabled " + error));
        CleanupStep("keyboard", CloseOwnKeyboard);
        CleanupStep("title", RestorePresetTitle);
        presetTitle = null;
        originalPresetTitle = null;
        CleanupStep("preview-position", () =>
        {
            if (shiftedDialog != null) shiftedDialog.anchoredPosition = originalDialogPosition;
        });
        shiftedDialog = null;
        CleanupStep("closing-preview-position", RestoreClosedSlotDialogPosition);
        closingShiftedDialog = null;
        CleanupStep("preview", () =>
        {
            if (objectPreview != null) UnityEngine.Object.Destroy(objectPreview);
        });
        objectPreview = null;
        previewRows = null;
        previewOrder = null;
        previewSlot = int.MinValue;
        previewLoadRequested = false;
        ResetMenuState();
        footerRefreshPending = footerRestorePending = false;
        // Do not close a dialog from inside its text/layout construction callback.
        // Tick keeps watching even when the requested page has not appeared yet.
    }

    private static void TickFaultCleanup()
    {
        if (faultCleanupFailed) return;
        try
        {
            if (dialogCloseTracker.IsClosing) return;
            CloseOwnedFaultDialog();
            if (!dialogCloseTracker.IsClosing && !faultFooterRestored) RestoreFaultFooter();
        }
        catch (Exception ex)
        {
            // An actual failure of the stock cleanup API must not loop each frame.
            faultCleanupFailed = true;
            CleanupStep("fault-cleanup-log", () => Plugin.Logger.LogWarning("BDP FaultCleanupFailed " + ex));
        }
    }

    internal static void CleanupStep(string name, Action action)
    {
        CleanupActions.Run(action, ex => Plugin.Logger.LogWarning("BDP CleanupFailed " + name + " " + ex));
    }

    private static void RestoreFaultFooter()
    {
        if (!SessionFaulted || editor == null || !editor.IsCustomMode || page == null ||
            !page.gameObject.activeInHierarchy || AppearanceBlocksPresets) return;
        var ui = GetUiManager();
        if (ui == null || ui.IsDialog) return;
        if (footer == null) footer = page.transform.root.GetComponentInChildren<UIMenuFooter>(true);
        if (footer == null || !footer.gameObject.activeInHierarchy) return;
        int id = editorGuideId >= 0 ? editorGuideId : footer.LastGuideId;
        if (id >= 0)
        {
            footer.SetGuide((KeyButtonGuideMasterId)(uint)id);
            faultFooterRestored = true;
        }
    }

    private static void CloseOwnedFaultDialog()
    {
        var ui = GetUiManager();
        if (ui == null || !ui.IsDialog || ui.DialogManager == null ||
            !ui.DialogManager.TryGetPage(ui.CurrentUIKey, out var current) ||
            current == null || !current.gameObject.activeInHierarchy) return;
        bool owned = false;
        if (ui.CurrentUIKey == UILoadKey.SelectDialog)
            owned = current.GetComponentsInChildren<UIDialogChoiceBar>(true).Any(bar =>
                bar != null && bar.cacheData != null &&
                (bar.cacheData.TextId == UiTextIds.MenuSave || bar.cacheData.TextId == UiTextIds.MenuInspect ||
                 (bar.cacheData.TextId >= UiTextIds.Slot && bar.cacheData.TextId < UiTextIds.Slot + SlotCount)));
        else
        {
            var dialog = current.TryCast<UIDefaultDialog>();
            owned = dialog != null && (dialog.infoId == UiTextIds.Notice || dialog.infoId == UiTextIds.DeleteConfirm);
        }
        if (!dialogCloseTracker.TryPrepare(owned, closeTicket =>
            Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Il2CppSystem.Action>((Action)(() =>
        {
            dialogCloseTracker.Complete(closeTicket);
        })), out var after)) return;
        faultFooterRestored = false;
        ui.CloseDialog(null, after, true, true, false);
    }
}
