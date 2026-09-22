namespace BazaarDecorPresets;

internal static class CleanupActions
{
    internal static void Run(Action action, Action<Exception> report)
    {
        try { action(); }
        catch (Exception ex)
        {
            // Cleanup and its diagnostics must never interrupt the remaining steps.
            try { report(ex); } catch { }
        }
    }
}
