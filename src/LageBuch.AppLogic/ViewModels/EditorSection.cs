using CommunityToolkit.Mvvm.ComponentModel;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>A category shown in the editor's left rail. Concrete kinds carry their own editor shape.</summary>
public abstract partial class EditorSection : ObservableObject
{
    private readonly Action<string, Action> _requestConfirm;

    /// <param name="title">The rail label.</param>
    /// <param name="requestConfirm">
    /// Asks before a filled row is removed (#543): the question, then what to run on confirm. When
    /// absent the row goes at once, which is what a section built outside the editor wants.
    /// </param>
    protected EditorSection(string title, Action<string, Action>? requestConfirm = null)
    {
        _title = title;
        _requestConfirm = requestConfirm ?? ((_, remove) => remove());
    }

    /// <summary>
    /// The rail label. Settable because a Checkliste's title is user data now: renaming one in its
    /// editor has to move through to the rail without rebuilding the section list.
    /// </summary>
    [ObservableProperty]
    private string _title;

    /// <summary>Raised after "+ HINZUFÜGEN" appended a row, so the view can put the caret in it (#543).</summary>
    public event EventHandler<RowAddedEventArgs>? RowAdded;

    protected void OnRowAdded(object row) => RowAdded?.Invoke(this, new RowAddedEventArgs(row));

    /// <summary>
    /// Removes a row after asking, like every other remove in the app (#543). A row still blank
    /// goes at once: there is nothing in it to lose, and asking would only get in the way of
    /// taking back a "+ HINZUFÜGEN" pressed once too often.
    /// </summary>
    /// <param name="name">What the row is called, for the question; blank when it has no name yet.</param>
    /// <param name="isBlank">True when every field the row offers for typing is empty.</param>
    /// <param name="remove">Takes the row out of the section.</param>
    protected void RemoveAfterConfirm(string? name, bool isBlank, Action remove)
    {
        ArgumentNullException.ThrowIfNull(remove);
        if (isBlank)
        {
            remove();
            return;
        }

        var question = string.IsNullOrWhiteSpace(name)
            ? $"Diesen Eintrag aus {Title} entfernen?"
            : $"„{name.Trim()}“ aus {Title} entfernen?";
        _requestConfirm(question, remove);
    }

    /// <summary>True when every one of a row's typed fields is empty or whitespace.</summary>
    protected static bool AllBlank(params string?[] values) => values.All(string.IsNullOrWhiteSpace);
}
