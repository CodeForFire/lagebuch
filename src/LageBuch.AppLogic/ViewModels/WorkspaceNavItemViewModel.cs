using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LageBuch.Persistence.MasterData;

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

    public WorkspaceNavItemViewModel(string header, object content, ChecklistViewModel? checklist = null, string? moduleKey = null, string? railKey = null)
    {
        Header = header;
        Content = content;
        ModuleKey = moduleKey ?? NavModules.Checklist;
        RailKey = railKey ?? ModuleKey;
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

    /// <summary>
    /// Which built-in module this entry is, or <see cref="NavModules.Checklist"/> for a Checkliste.
    /// The narrow layout's bottom bar keys its icon off this: a rail label alone is not legible in
    /// a 82dp cell, and a Checkliste is user-named so it can only have the generic glyph.
    /// </summary>
    public string ModuleKey { get; }

    /// <summary>
    /// The key this Einsatz's rail overrides use for the entry (<c>NavigationLayout.KeyOf</c>): the
    /// module key, or the Checkliste's id. Unlike <see cref="ModuleKey"/> it tells two Checklisten
    /// apart, so a rebuilt rail finds the entry that was open by it.
    /// </summary>
    public string RailKey { get; }

    /// <summary>
    /// Whether „Ausblenden“ is offered: never for the ETB, never in a read-only Einsatz, and not
    /// while the module holds live work (an active Trupp, an open Aufgabe). Kept fresh by the
    /// workspace, which is where all three are known.
    /// </summary>
    [ObservableProperty]
    private bool _canHide;

    /// <summary>
    /// The global shortcut that opens this entry, shown on its rail tab (#544): a keyboard user
    /// never hovers, so a tooltip would not tell them. It follows the entry's position on the rail;
    /// null past the tenth, which has no digit left.
    /// </summary>
    [ObservableProperty]
    private string? _shortcutHint;

    /// <summary>
    /// Whether this is the entry currently open. The wide rail gets this from
    /// <c>TabItem:selected</c>, but the narrow bottom bar is an <c>ItemsControl</c> of buttons
    /// rather than a selector, so it needs the flag on the item itself.
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>True for a Checkliste tab, false for a built-in module.</summary>
    public bool IsChecklist => _checklist is not null;

    /// <summary>A Checkliste whose mandatory items are all ticked. Always false for a module.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AccessibleName))]
    private bool _isComplete;

    /// <summary>A Checkliste with mandatory items still open. Always false for a module.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AccessibleName))]
    private bool _isIncomplete;

    /// <summary>
    /// What a screen reader announces for the tab: the header, plus the Checkliste's state that the
    /// rail otherwise shows only as the colour of a dot (#670).
    /// </summary>
    public string AccessibleName =>
        IsIncomplete ? $"{Header} – unvollständig"
        : IsComplete ? $"{Header} – vollständig"
        : Header;

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
