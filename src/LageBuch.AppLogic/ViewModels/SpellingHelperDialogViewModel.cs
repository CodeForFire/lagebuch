using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// The Buchstabier-Helfer as an overlay over the ETB (#592), for spelling a name while writing an
/// entry without leaving the journal. Nothing to save: closing it is all it does.
/// </summary>
public sealed partial class SpellingHelperDialogViewModel : ObservableObject
{
    public SpellingHelperViewModel Helper { get; } = new();

    public event EventHandler? Closed;

    [RelayCommand]
    private void Close() => Closed?.Invoke(this, EventArgs.Empty);
}
