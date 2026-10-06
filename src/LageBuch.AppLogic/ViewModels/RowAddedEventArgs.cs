namespace LageBuch.AppLogic.ViewModels;

/// <summary>The row a Stammdaten section's "+ HINZUFÜGEN" just appended (#543).</summary>
public sealed class RowAddedEventArgs(object row) : EventArgs
{
    /// <summary>The new row, as it sits in the section's collection.</summary>
    public object Row { get; } = row;
}
