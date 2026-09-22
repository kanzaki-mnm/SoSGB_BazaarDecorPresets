namespace BazaarDecorPresets;

internal enum LayoutApplyStatus { Applied, Restored, RecoveryUnconfirmed }

internal sealed class LayoutApplyResult
{
    internal LayoutApplyStatus Status;
    internal readonly List<Exception> Errors = new();
    internal List<Slot> ModelsToRefresh = new();
}

// Game calls are injected so partial writes and failed recovery can be tested
// without touching a player's layout or save file.
internal static class LayoutTransaction
{
    internal static LayoutApplyResult Apply(IReadOnlyList<Slot> before, IReadOnlyList<Slot> plan,
        Action<Slot> set, Func<List<Slot>> snapshot)
    {
        var result = new LayoutApplyResult();
        try
        {
            foreach (var slot in plan)
            {
                var old = before.First(item => item.Category == slot.Category && item.Index == slot.Index);
                if (old.ItemId == slot.ItemId) continue;
                set(slot);
                result.ModelsToRefresh.Add(slot);
            }
            if (!SameLayout(plan, snapshot())) throw new InvalidOperationException("Editor did not accept the planned layout.");
            result.Status = LayoutApplyStatus.Applied;
            return result;
        }
        catch (Exception ex) { result.Errors.Add(ex); }

        // A setter may change state and then throw. Restore every original slot,
        // including the failing call, and do not stop at the first recovery error.
        foreach (var slot in before)
        {
            try { set(slot); }
            catch (Exception ex) { result.Errors.Add(ex); }
        }
        result.Status = LayoutApplyStatus.RecoveryUnconfirmed;
        try
        {
            if (SameLayout(before, snapshot())) result.Status = LayoutApplyStatus.Restored;
            else result.Errors.Add(new InvalidOperationException("Recovered layout differs from the pre-load snapshot."));
        }
        catch (Exception ex) { result.Errors.Add(ex); }
        // Only refresh a layout that was actually verified. Failed setters can
        // have affected previews even if they never returned successfully.
        result.ModelsToRefresh = result.Status == LayoutApplyStatus.Restored ? before.ToList() : new List<Slot>();
        return result;
    }

    private static bool SameLayout(IReadOnlyList<Slot> expected, IReadOnlyList<Slot> actual)
    {
        if (actual == null) return false;
        PresetStorage.ValidateSlots(actual);
        return expected.Count == actual.Count && expected.All(item => actual.Any(other => item.Equals(other)));
    }
}
