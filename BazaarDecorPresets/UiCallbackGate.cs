namespace BazaarDecorPresets;

// UI notifications may arrive after navigation or re-enter during stock calls.
// Consume before running side effects; replacing the request retires its callbacks.
internal sealed class UiCallbackGate
{
    private long version;
    private bool pending;

    internal long Begin()
    {
        pending = true;
        return ++version;
    }

    internal bool TryConsume(long ticket)
    {
        if (!pending || ticket != version) return false;
        pending = false;
        return true;
    }

    internal void Invalidate()
    {
        pending = false;
        version++;
    }
}
