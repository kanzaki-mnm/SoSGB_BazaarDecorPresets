using BokuMono;
using UnityEngine;

namespace BazaarDecorPresets;

// Read-only evidence for the later application phase.  These hooks only log
// calls made by the currently observed editor and never invoke game APIs.
internal static class LayoutDiagnostics
{
    private static int session;
    private static int sequence;
    private static BazaarManager trackedManager;
    private static int closeObserveUntilFrame = -1;

    internal static void Begin(BazaarManager manager)
    {
        if (trackedManager != null) End("new editor session");
        session++;
        sequence = 0;
        trackedManager = manager;
        closeObserveUntilFrame = -1;
        Plugin.Logger.LogInfo($"BDP DiagnosticBegin session={session}");
    }

    internal static void EditorClosing(BazaarManager manager, string reason, string layout)
    {
        if (!IsTracked(manager)) return;
        // SaveAndClose starts the game's model and buff work after the editor
        // prefix has run. Keep this one manager under read-only observation for
        // five seconds instead of ending the trace before that work begins.
        closeObserveUntilFrame = Time.frameCount + 300;
        Plugin.Logger.LogInfo($"BDP DiagnosticClosing session={session} reason={reason} frame={Time.frameCount} layout={layout}");
    }

    internal static void Tick()
    {
        if (trackedManager != null && closeObserveUntilFrame >= 0 && Time.frameCount >= closeObserveUntilFrame)
            End("post-close observation complete");
    }

    private static bool IsTracked(BazaarManager manager) => manager != null && manager == trackedManager;
    internal static bool IsTraceActive => trackedManager != null &&
        (closeObserveUntilFrame < 0 || Time.frameCount < closeObserveUntilFrame);

    private static void End(string reason)
    {
        if (trackedManager == null) return;
        Plugin.Logger.LogInfo($"BDP DiagnosticEnd session={session} reason={reason}");
        trackedManager = null;
        closeObserveUntilFrame = -1;
    }

    internal static void SetCustomParts(BazaarManager manager, uint itemId, BazaarCustomItemData.PartsCategory category, int index)
    {
        if (!IsTracked(manager)) return;
        Plugin.Logger.LogInfo($"BDP Diagnostic#{++sequence} SetCustomParts frame={Time.frameCount} item={itemId} category={category} index={index} layout={NativePresetUi.DescribeObservedLayout(manager)}");
    }

    internal static void ModelLoad(BazaarManager manager)
    {
        if (!IsTracked(manager)) return;
        Plugin.Logger.LogInfo($"BDP Diagnostic#{++sequence} LoadBazaarCustomPartsModel frame={Time.frameCount}");
    }

    internal static void SetupBuff(BazaarManager manager)
    {
        if (!IsTracked(manager)) return;
        Plugin.Logger.LogInfo($"BDP Diagnostic#{++sequence} SetupPartsBuff frame={Time.frameCount}");
    }

    internal static void ReadEffects(BazaarManager manager)
    {
        if (!IsTracked(manager)) return;
        Plugin.Logger.LogInfo($"BDP Diagnostic#{++sequence} GetEditCustomPartsEffectList frame={Time.frameCount}");
    }

    internal static void ShopModel(string method, uint itemId, BazaarCustomItemData.PartsCategory category, int index, bool isCreate, bool isEdit)
    {
        if (!IsTraceActive) return;
        Plugin.Logger.LogInfo($"BDP Diagnostic#{++sequence} {method} frame={Time.frameCount} item={itemId} category={category} index={index} create={isCreate} edit={isEdit}");
    }
}
