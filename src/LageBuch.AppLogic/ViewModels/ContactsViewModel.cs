using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.AppLogic.Services;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// Searchable view of the Personal Stammdaten, plus every Fahrzeug that carries a phone or an E-Mail, shown as a
/// read-only tab in the incident workspace.
/// It answers the question an Einsatzleitung actually asks over the radio — "wer ist zuständig für
/// ABC?" — and then lets that person be reached. Like <see cref="LinksViewModel"/> it holds no
/// session and mutates nothing: the roster is global master data, not incident state, so phoning
/// somebody off it is not an incident action and nothing is written to the ETB.
/// </summary>
public sealed partial class ContactsViewModel : ObservableObject, INarrowAware
{
    // ContactEntry is a record, so two contacts with identical fields are equal by value. A
    // dictionary keyed on it would collapse them; a parallel array cannot, and costs nothing at
    // roster size (the Landkreis roster is ~35 people).
    private readonly (ContactEntry Entry, string Haystack)[] _entries;
    private readonly IFileDialogService _dialogs;

    // Which vehicles are listed is ContactEntry.Roster's rule, shared with the Ausgabeliste.
    public ContactsViewModel(IReadOnlyList<Person> people, IFileDialogService dialogs, IReadOnlyList<Vehicle>? vehicles = null)
    {
        ArgumentNullException.ThrowIfNull(people);
        _dialogs = dialogs;
        Contacts = ContactEntry.Roster(people, vehicles);
        _entries = Contacts.Select(c => (c, HaystackFor(c))).ToArray();
        VisibleContacts = new ObservableCollection<ContactRow>(Contacts.Select(c => RowFor(c, Array.Empty<string>())));
    }

    public IReadOnlyList<ContactEntry> Contacts { get; }

    /// <summary>
    /// The subset of <see cref="Contacts"/> matching <see cref="FilterText"/>, and what the view
    /// renders. <see cref="Contacts"/> stays the full set so "Keine Kontakte hinterlegt" (nothing
    /// in the Stammdaten) stays distinguishable from "nothing matched what you typed" — the same
    /// split <see cref="LinksViewModel"/> uses.
    /// </summary>
    public ObservableCollection<ContactRow> VisibleContacts { get; }

    /// <summary>
    /// Whether the tab is laid out for a phone. The view reads it to leave the search box
    /// unfocused there, where focusing it would push the soft keyboard over the list.
    /// </summary>
    [ObservableProperty]
    private bool _isNarrow;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFiltered))]
    [NotifyPropertyChangedFor(nameof(NoMatchesMessage))]
    [NotifyPropertyChangedFor(nameof(MatchCountText))]
    private string _filterText = string.Empty;

    public bool IsFiltered => !string.IsNullOrWhiteSpace(FilterText);

    public string NoMatchesMessage => $"Kein Kontakt passt zu „{FilterText.Trim()}“.";

    /// <summary>"2 von 35": how far the search narrowed the roster, shown while one is typed.</summary>
    public string MatchCountText =>
        string.Create(CultureInfo.InvariantCulture, $"{VisibleContacts.Count} von {Contacts.Count}");

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    /// <summary>
    /// Everything about one contact that is worth searching, joined into a single string.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The Notiz is the field this whole tab exists for. It carries either a Fachgebiet ("KBM
    /// Gefahrgut, Fachberater Arbeits- und Gesundheitsschutz") or the Feuerwehren somebody covers
    /// ("Alling, Biburg, Eichenau"), so it is what turns "wer kann ABC?" into a name.
    /// </para>
    /// <para>
    /// Joined with a newline rather than a space so a term can never match across a field
    /// boundary: "ZF Land" must not find a Rolle of "ZF" sitting next to a Funkrufname of
    /// "Land 1".
    /// </para>
    /// <para>
    /// The number is added a second time with its separators stripped. The roster writes numbers
    /// for a human to read ("01 71 / 6 53 58 23"), so an operator typing the digits they would
    /// dial would otherwise match nothing.
    /// </para>
    /// </remarks>
    private static string HaystackFor(ContactEntry c)
    {
        var fields = new List<string?> { c.Name, c.Caption, c.CallSign, c.Phone, c.Email, c.Note };
        var digits = new string((c.Phone ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        if (digits.Length > 0)
        {
            fields.Add(digits);
        }

        return string.Join('\n', fields.Where(s => !string.IsNullOrWhiteSpace(s)));
    }

    /// <summary>
    /// Splits the query on whitespace and keeps a contact only when <em>every</em> term appears
    /// somewhere in their fields.
    /// </summary>
    /// <remarks>
    /// Term-wise rather than one substring, because the Notiz is prose: it reads "KBM Gefahrgut,
    /// Fachberater Arbeits- und Gesundheitsschutz", and "gefahrgut kbm" or "mustermann gefahrgut"
    /// have to find it just as "gefahrgut" does. Ordinal and case-insensitive, matching
    /// <see cref="LinksViewModel"/> and the app's AutoCompleteBoxes. An empty query leaves no
    /// terms, and All over an empty sequence is true, so the whole roster shows.
    /// </remarks>
    private void ApplyFilter()
    {
        var terms = FilterText.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        VisibleContacts.Clear();
        foreach (var (entry, haystack) in _entries)
        {
            if (terms.All(t => haystack.Contains(t, StringComparison.OrdinalIgnoreCase)))
            {
                VisibleContacts.Add(RowFor(entry, terms));
            }
        }
    }

    /// <summary>
    /// Only the name and the Notiz are split for highlighting: they are the two lines a hit has
    /// to explain. A hit in the Rolle or the Funkrufname is already plain to see next to the name.
    /// </summary>
    private static ContactRow RowFor(ContactEntry entry, IReadOnlyList<string> terms) =>
        new(entry, TextSegment.Highlight(entry.Name, terms), TextSegment.Highlight(entry.Note, terms));

    [RelayCommand]
    private void ClearFilter() => FilterText = string.Empty;

    /// <summary>
    /// A roster address comes from an imported Stammdaten file, not from what the operator typed
    /// here, so it is validated before it reaches any platform launcher — see
    /// <see cref="MailAddressValidator"/> for what a CR/LF or a '?' in one would otherwise do.
    /// </summary>
    [RelayCommand]
    private async Task MailAsync(ContactEntry contact)
    {
        ArgumentNullException.ThrowIfNull(contact);
        ErrorMessage = await ContactLauncher.TryMailAsync(_dialogs, contact.Email, contact.Name);
    }

    /// <inheritdoc cref="MailAsync"/>
    [RelayCommand]
    private async Task CallAsync(ContactEntry contact)
    {
        ArgumentNullException.ThrowIfNull(contact);
        ErrorMessage = await ContactLauncher.TryCallAsync(_dialogs, contact.Phone, contact.Name);
    }
}
