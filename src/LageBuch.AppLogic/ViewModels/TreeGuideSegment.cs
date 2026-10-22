namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// One cell of the guide lines in front of a Funktion in the Führungsstruktur (#703): what is drawn
/// for one level above the row. The view draws the lines; <see cref="Text"/> spells the same cell
/// out for tests and screen readers.
/// </summary>
public sealed class TreeGuideSegment
{
    /// <summary>Nothing: the level above has no more rows below this one.</summary>
    public static readonly TreeGuideSegment Blank = new(lineAbove: false, lineBelow: false, branch: false, "   ");

    /// <summary>A line straight down, past this row to a later row of the level above.</summary>
    public static readonly TreeGuideSegment Pass = new(lineAbove: true, lineBelow: true, branch: false, "│  ");

    /// <summary>This row, with more rows under the same superior after it.</summary>
    public static readonly TreeGuideSegment Tee = new(lineAbove: true, lineBelow: true, branch: true, "├─ ");

    /// <summary>This row, the last under its superior.</summary>
    public static readonly TreeGuideSegment Elbow = new(lineAbove: true, lineBelow: false, branch: true, "└─ ");

    private TreeGuideSegment(bool lineAbove, bool lineBelow, bool branch, string text)
    {
        LineAbove = lineAbove;
        LineBelow = lineBelow;
        Branch = branch;
        Text = text;
    }

    /// <summary>The vertical line through the upper half of the row.</summary>
    public bool LineAbove { get; }

    /// <summary>The vertical line through the lower half of the row.</summary>
    public bool LineBelow { get; }

    /// <summary>The horizontal line out to the Funktion.</summary>
    public bool Branch { get; }

    public string Text { get; }
}
