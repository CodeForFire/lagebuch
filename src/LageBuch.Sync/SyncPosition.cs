namespace LageBuch.Sync;

/// <summary>
/// Where a snapshot sits in the host's history: the host's <see cref="Epoch"/> — a fresh random
/// value every time it starts sharing — and its change counter within that epoch (#295).
/// </summary>
/// <remarks>
/// The one decision a joined client makes about ordering lives in <see cref="Supersedes"/>, kept pure
/// so it can be tested exhaustively without a host. The same state reaches a client over three
/// channels — the hub push, the command's own response, a <c>GET /snapshot</c> resync — and nothing
/// orders them against each other, so the client has to.
/// </remarks>
internal sealed record SyncPosition(Guid Epoch, long Revision)
{
    public static SyncPosition Of(IncidentSnapshot snapshot) => new(snapshot.Epoch, snapshot.Revision);

    public static SyncPosition Of(RevisionInfo info) => new(info.Epoch, info.Revision);

    /// <summary>
    /// Whether a snapshot at this position should replace one held at <paramref name="held"/>.
    /// </summary>
    /// <remarks>
    /// Within one epoch: newer only, so a duplicate or an overtaken copy is dropped. Across epochs the
    /// two counters are unrelated numbers, so a different epoch always wins. Comparing bare counters
    /// instead would let a host that restarted and happened to reach the client's old revision look
    /// current. A stale copy from an <em>older</em> epoch arriving late would also win; that is
    /// accepted, because the next reconcile pass finds the mismatch and fetches the true state.
    /// </remarks>
    public bool Supersedes(SyncPosition held)
    {
        ArgumentNullException.ThrowIfNull(held);
        return Epoch != held.Epoch || Revision > held.Revision;
    }
}
