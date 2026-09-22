namespace BazaarDecorPresets;

// Closing is asynchronous; a fault must not start another close on the same UI.
internal sealed class DialogCloseTracker
{
    private int version;
    internal bool IsClosing { get; private set; }
    internal bool TryPrepare<T>(bool owned, Func<int, T> prepare, out T callback)
    {
        callback = default;
        if (!TryBegin(owned, out int ticket)) return false;
        try { callback = prepare(ticket); }
        catch
        {
            // No stock close has been requested yet. Release only this reservation.
            Complete(ticket);
            throw;
        }
        return true;
    }
    internal bool TryBegin(bool owned, out int ticket)
    {
        ticket = version;
        if (!owned || IsClosing) return false;
        IsClosing = true;
        ticket = ++version;
        return true;
    }
    internal bool Complete(int ticket)
    {
        if (!IsClosing || ticket != version) return false;
        IsClosing = false;
        return true;
    }
    internal void Reset() { version++; IsClosing = false; }
}
