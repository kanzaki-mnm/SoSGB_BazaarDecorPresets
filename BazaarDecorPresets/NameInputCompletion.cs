namespace BazaarDecorPresets;

internal static class NameInputCompletion
{
    internal static bool Complete(bool success, ref bool inputOpen, ref bool footerRequested, ref bool returnToSlots)
    {
        // Cancellation needs the original ownership state before it is cleared.
        // Keep an already queued return intact when a second notification arrives.
        if (!success && !inputOpen) return false;
        inputOpen = false;
        footerRequested = false;
        if (!success) returnToSlots = true;
        return success;
    }
}
