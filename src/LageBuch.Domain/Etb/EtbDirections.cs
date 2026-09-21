namespace LageBuch.Domain.Etb;

/// <summary>
/// Rules shared by every consumer of <see cref="EtbDirection"/>, so the "which directions are
/// machine-written?" question is answered in one place instead of drifting between the domain
/// and the view model.
/// </summary>
public static class EtbDirections
{
    /// <summary>
    /// Written by the app, never chosen by a human: omitted from the direction picker and never
    /// editable after the fact. Deliberately *not* the same question as "may it be hidden" --
    /// <see cref="EtbDirection.Measurement"/> is app-written yet always visible (#424), which is
    /// why the ETB's filter tests for <see cref="EtbDirection.System"/> on its own.
    /// </summary>
    public static bool IsAppWritten(EtbDirection direction) =>
        direction is EtbDirection.System or EtbDirection.Measurement;
}
