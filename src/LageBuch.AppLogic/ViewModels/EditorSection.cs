using CommunityToolkit.Mvvm.ComponentModel;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>A category shown in the editor's left rail. Concrete kinds carry their own editor shape.</summary>
public abstract partial class EditorSection : ObservableObject
{
    protected EditorSection(string title) => _title = title;

    /// <summary>
    /// The rail label. Settable because a Checkliste's title is user data now: renaming one in its
    /// editor has to move through to the rail without rebuilding the section list.
    /// </summary>
    [ObservableProperty]
    private string _title;
}
