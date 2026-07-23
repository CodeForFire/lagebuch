using CommunityToolkit.Mvvm.ComponentModel;

namespace Feuerwehr.AppLogic.ViewModels;

/// <summary>A category shown in the editor's left rail. Concrete kinds carry their own editor shape.</summary>
public abstract class EditorSection : ObservableObject
{
    protected EditorSection(string title) => Title = title;

    public string Title { get; }
}
