namespace Channels.Networking.Sockets.Internal
{
    /// <summary>
    /// Used by Signal to control how callbacks are invoked
    /// </summary>
    internal enum ContinuationMode
    {
        Synchronous,
        ThreadPool,
        // TODO: sync-context? but if so: whose? the .Current at creation? at SetResult?
    }
}
