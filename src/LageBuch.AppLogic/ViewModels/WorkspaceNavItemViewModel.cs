using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// One entry of the Einsatz workspace's left rail: its label, the module view model behind it,
/// and — for a Checkliste — whether its mandatory items are all ticked.
/// </summary>
/// <remarks>
/// The rail used to be ten literal TabItems, each binding its own module property. It is now
/// ItemsSource-driven, so one header template serves every tab and needs plain values rather than
/// a per-type binding path.
/// <para>
/// <see cref="IsComplete"/>/<see cref="IsIncomplete"/> are real properties kept fresh by
/// forwarding the wrapped checklist's notifications, not a binding through a nullable. A binding
/// that resolves to nothing leaves <c>IsVisible</c> at its default <c>true</c>, which would put a
/// status dot on every module tab.
/// </para>
/// </remarks>
public sealed partial class WorkspaceNavItemViewModel : ObservableObject, IDisposable
{
    private readonly ChecklistViewModel? _checklist;

    public WorkspaceNavItemViewModel(string header, object content, ChecklistViewModel? checklist = null)
    {
        Header = header;
        Content = content;
        _checklist = checklist;
        if (_checklist is not null)
        {
            _checklist.PropertyChanged += OnChecklistChanged;
            UpdateCompletion();
        }
    }

    /// <summary>The rail label, upper-cased as every tab header has always been.</summary>
    public string Header { get; }

    /// <summary>The module view model; the ViewLocator resolves its view, exactly as before.</summary>
    public object Content { get; }

    /// <summary>True for a Checkliste tab, false for a built-in module.</summary>
    public bool IsChecklist => _checklist is not null;

    /// <summary>A Checkliste whose mandatory items are all ticked. Always false for a module.</summary>
    [ObservableProperty]
    private bool _isComplete;

    /// <summary>A Checkliste with mandatory items still open. Always false for a module.</summary>
    [ObservableProperty]
    private bool _isIncomplete;

    /// <summary>
    /// Upper-cases a Checkliste's name for the rail. <c>ToUpperInvariant</c> turns "ß" into "SS",
    /// which is correct German casing and the only sensible thing here — the alternative is a
    /// per-character special case for a label nobody reads letter by letter.
    /// </summary>
    public static string HeaderFor(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        return title.ToUpperInvariant();
    }

    public void Dispose()
    {
        if (_checklist is not null)
        {
            _checklist.PropertyChanged -= OnChecklistChanged;
        }
    }

    private void OnChecklistChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or nameof(ChecklistViewModel.AllMandatoryDone))
        {
            UpdateCompletion();
        }
    }

    private void UpdateCompletion()
    {
        IsComplete = _checklist!.AllMandatoryDone;
        IsIncomplete = !_checklist.AllMandatoryDone;
    }
}
