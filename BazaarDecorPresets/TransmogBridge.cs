using System.Reflection;
using BepInEx.Unity.IL2CPP;

namespace BazaarDecorPresets;

internal static class TransmogBridge
{
    internal const string PluginGuid = "com.icy.bazaardecortransmog";
    private static Func<bool> readActive;
    private static bool legacyFallback, failed;

    internal static void Initialize()
    {
        try
        {
            if (!IL2CPPChainloader.Instance.Plugins.TryGetValue(PluginGuid, out var plugin)) return;
            var api = plugin.Instance.GetType().Assembly.GetType("BazaarDecorTransmog.BazaarDecorTransmogApi");
            var version = api?.GetField("ApiVersion", BindingFlags.Public | BindingFlags.Static);
            var getter = api?.GetProperty("IsAppearanceEditorActive", BindingFlags.Public | BindingFlags.Static)?.GetMethod;
            if (version?.GetRawConstantValue() is not int value || value != 1 ||
                getter == null || getter.ReturnType != typeof(bool) || getter.GetParameters().Length != 0)
            {
                legacyFallback = true;
                Plugin.Logger.LogWarning("BDP Transmog API unavailable/unsupported; using limited Japanese/English title fallback.");
                return;
            }
            readActive = (Func<bool>)getter.CreateDelegate(typeof(Func<bool>));
            Plugin.Logger.LogInfo("BDP Transmog API v1 connected.");
        }
        catch (Exception ex)
        {
            failed = true;
            Plugin.Logger.LogWarning("BDP Transmog API initialization failed; preset entry disabled: " + ex.Message);
        }
    }

    internal static bool BlocksPresets(Func<bool> legacyTitleCheck)
    {
        if (failed) return true;
        if (readActive == null) return legacyFallback && legacyTitleCheck();
        try { return readActive(); }
        catch (Exception ex)
        {
            // An unreadable state is not permission to edit real decor.
            failed = true;
            Plugin.Logger.LogWarning("BDP Transmog API read failed; preset entry disabled: " + ex.Message);
            return true;
        }
    }
}
