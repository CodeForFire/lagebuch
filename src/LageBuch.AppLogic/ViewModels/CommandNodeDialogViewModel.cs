using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.AppLogic.Services;
using LageBuch.Domain.Leadership;
using LageBuch.Persistence.MasterData;
using LageBuch.Sync;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// The Führung board's one form: forms a Bereitstellung, Abschnitt, Unterabschnitt or partner
/// Einsatzleitung with all its details, edits an existing node, or edits the Einsatzleitung
/// Feuerwehr's own post (Standort, Rufname, Telefon, Funkgruppe). Buffered until the primary button,
/// with the Einsatzdaten dialog's contract: a discard question on ABBRECHEN after typing (#652) and
/// <see cref="Closed"/> on every outcome so the host clears the overlay.
/// </summary>
/// <remarks>
/// The Domain is the last guard for lengths and names; this dialog pre-checks what it refuses (a blank
/// or taken name, a node or an Unterabschnitt's Abschnitt a peer dissolved meanwhile) so the
/// Lagebuchführer sees a field error rather than an exception. The caps are enforced as
/// <c>MaxLength</c> in the view.
/// </remarks>
public sealed partial class CommandNodeDialogViewModel : ObservableObject
{
    private enum Mode
    {
        Create,
        Edit,
        Post,
    }

    private readonly IIncidentSession _session;
    private readonly Action? _onChanged;
    private readonly Mode _mode;
    private readonly CommandNodeKind _kind;
    private readonly Guid? _parentId;
    private readonly Guid? _nodeId;

    // What the fields held when the dialog opened, in the form they would be saved: HasChanges is
    // "did the Lagebuchführer type anything", so a peer's edit to the node meanwhile, or a
    // Funkgruppe the record spells off-catalogue, must not read as one.
    private Fields _seeded;

    private CommandNodeDialogViewModel(
        IIncidentSession session,
        MasterDataSet masterData,
        Mode mode,
        CommandNodeKind kind,
        Guid? parentId,
        Guid? nodeId,
        string name,
        Action? onChanged)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(masterData);
        _session = session;
        _onChanged = onChanged;
        _mode = mode;
        _kind = kind;
        _parentId = parentId;
        _nodeId = nodeId;
        _name = name;
        FunkgruppeOptions = [.. masterData.TalkGroups.Select(g => g.Display)];
    }

    public event EventHandler? Closed;

    /// <summary>The Funkgruppen of the Stammdaten, offered as suggestions and never enforced.</summary>
    public IReadOnlyList<string> FunkgruppeOptions { get; }

    public string Title { get; private init; } = string.Empty;

    /// <summary>The label of the primary button.</summary>
    public string PrimaryText { get; private init; } = "SPEICHERN";

    /// <summary>False when editing the Einsatzleitung's own post: it has no name.</summary>
    public bool ShowName => _mode != Mode.Post;

    /// <summary>False when editing the Einsatzleitung's own post: it has no Auftrag.</summary>
    public bool ShowAuftrag => _mode != Mode.Post;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string? _auftrag;

    [ObservableProperty]
    private string? _standort;

    [ObservableProperty]
    private string? _funkgruppe;

    [ObservableProperty]
    private string? _rufname;

    [ObservableProperty]
    private string? _telefon;

    /// <summary>Why the Name cannot be used, in German; null when it can.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string? _nameError;

    /// <summary>Why the last save was refused, in German; null otherwise.</summary>
    [ObservableProperty]
    private string? _saveError;

    /// <summary>Set while ABBRECHEN asks whether typed details may go (#652); SPEICHERN waits for the answer.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isDiscardConfirmOpen;

    /// <summary>Opens the dialog to form a node of <paramref name="kind"/> (under <paramref name="parentId"/> for an Unterabschnitt).</summary>
    public static CommandNodeDialogViewModel ForCreate(
        IIncidentSession session,
        MasterDataSet masterData,
        CommandNodeKind kind,
        Guid? parentId,
        string suggestedName,
        Action? onChanged = null) =>
        new CommandNodeDialogViewModel(session, masterData, Mode.Create, kind, parentId, null, suggestedName ?? string.Empty, onChanged)
        {
            Title = kind switch
            {
                CommandNodeKind.Unterabschnitt => "Unterabschnitt bilden",
                CommandNodeKind.Bereitstellung => "Bereitstellung einrichten",
                CommandNodeKind.Partner => "Einsatzleitung eintragen",
                _ => "Abschnitt bilden",
            },
            PrimaryText = kind switch
            {
                CommandNodeKind.Partner => "EINTRAGEN",
                CommandNodeKind.Bereitstellung => "EINRICHTEN",
                _ => "BILDEN",
            },
        }.Seeded();

    /// <summary>Opens the dialog on an existing node; a node that is gone shows an error on save instead of throwing.</summary>
    public static CommandNodeDialogViewModel ForEdit(
        IIncidentSession session, MasterDataSet masterData, Guid nodeId, Action? onChanged = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        var node = session.Incident.CommandNodes.FirstOrDefault(n => n.Id == nodeId);
        var kind = node?.Kind ?? CommandNodeKind.Abschnitt;
        return new CommandNodeDialogViewModel(session, masterData, Mode.Edit, kind, node?.ParentId, nodeId, node?.Name ?? string.Empty, onChanged)
        {
            Title = $"{CommandNodeKindLabels.Of(kind)} bearbeiten",
            Auftrag = node?.Auftrag,
            Standort = node?.Standort,
            Funkgruppe = node?.Funkgruppe,
            Rufname = node?.Rufname,
            Telefon = node?.Telefon,
        }.Seeded();
    }

    /// <summary>Opens the dialog on the Einsatzleitung Feuerwehr's own post.</summary>
    public static CommandNodeDialogViewModel ForPost(
        IIncidentSession session, MasterDataSet masterData, Action? onChanged = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        var post = session.Incident.LeadershipPost;
        return new CommandNodeDialogViewModel(session, masterData, Mode.Post, CommandNodeKind.Abschnitt, null, null, string.Empty, onChanged)
        {
            Title = "Einsatzleitung Feuerwehr",
            Standort = post.Standort,
            Funkgruppe = post.Funkgruppe,
            Rufname = post.Rufname,
            Telefon = post.Telefon,
        }.Seeded();
    }

    /// <summary>
    /// Whether any field differs from what the dialog opened with. Deliberately not compared with the
    /// live record: a peer's edit meanwhile is not something typed here, and must neither ask the
    /// discard question nor be overwritten by an untouched save.
    /// </summary>
    public bool HasChanges => Current() != _seeded;

    private bool CanSave => !_session.IsReadOnly && !IsDiscardConfirmOpen && NameError is null;

    private CommandNode? LiveNode =>
        _session.Incident.CommandNodes.FirstOrDefault(n => n.Id == _nodeId && n.IsLive);

    private string? FunkgruppeToSave => Normalize(StammdatenCatalogue.Normalize(Funkgruppe, FunkgruppeOptions));

    partial void OnNameChanged(string value) => NameError = ValidateName();

    private string? ValidateName()
    {
        if (!ShowName)
        {
            return null;
        }

        var name = Name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return "Name fehlt.";
        }

        var taken = _session.Incident.CommandNodes.Any(n =>
            n.IsLive && n.Id != _nodeId && n.ParentId == _parentId
            && string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase));
        return taken ? "Name ist bereits vergeben." : null;
    }

    private Fields Current() =>
        new(Name?.Trim() ?? string.Empty, Normalize(Auftrag), Normalize(Standort), FunkgruppeToSave, Normalize(Rufname), Normalize(Telefon));

    private CommandNodeDialogViewModel Seeded()
    {
        _seeded = Current();
        return this;
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        SaveError = null;
        NameError = ValidateName();
        if (NameError is not null)
        {
            return;
        }

        try
        {
            if (!Apply())
            {
                return;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or KeyNotFoundException)
        {
            SaveError = DomainRefusal.Message(ex);
            return;
        }

        Closed?.Invoke(this, EventArgs.Empty);
    }

    // Returns false when the save was refused with SaveError set; true when it went through or had
    // nothing to write.
    private bool Apply()
    {
        var typed = Current();
        switch (_mode)
        {
            case Mode.Create:
                if (ParentIsGone())
                {
                    SaveError = "Dieser Abschnitt wurde inzwischen aufgelöst.";
                    return false;
                }

                _session.AddCommandNode(
                    _kind, typed.Name, _parentId, typed.Auftrag, typed.Standort, typed.Funkgruppe, typed.Rufname, typed.Telefon);
                _onChanged?.Invoke();
                return true;
            case Mode.Edit:
                if (LiveNode is not { } node)
                {
                    SaveError = "Dieser Abschnitt wurde inzwischen aufgelöst.";
                    return false;
                }

                if (HasChanges)
                {
                    var save = Merge(typed, Seen(node.Name, node.Auftrag, node.Standort, node.Funkgruppe, node.Rufname, node.Telefon));
                    _session.UpdateCommandNode(node.Id, save.Name, save.Auftrag, save.Standort, save.Funkgruppe, save.Rufname, save.Telefon);
                    _onChanged?.Invoke();
                }

                return true;
            default:
                if (HasChanges)
                {
                    var post = _session.Incident.LeadershipPost;
                    var save = Merge(typed, Seen(string.Empty, null, post.Standort, post.Funkgruppe, post.Rufname, post.Telefon));
                    _session.UpdateLeadershipPost(save.Standort, save.Rufname, save.Telefon, save.Funkgruppe);
                    _onChanged?.Invoke();
                }

                return true;
        }
    }

    // The live record in the form this dialog seeds and saves its fields, so it lines up with _seeded.
    private Fields Seen(string name, string? auftrag, string? standort, string? funkgruppe, string? rufname, string? telefon) =>
        new(
            name.Trim(),
            Normalize(auftrag),
            Normalize(standort),
            Normalize(StammdatenCatalogue.Normalize(funkgruppe, FunkgruppeOptions)),
            Normalize(rufname),
            Normalize(telefon));

    // A save writes every field, so one the Lagebuchführer left alone takes the live value: a peer
    // may have changed it meanwhile, and renaming the node must not put back the Auftrag the dialog
    // opened with. A field typed here wins, peer or not.
    private Fields Merge(Fields typed, Fields live) => new(
        Pick(typed.Name, _seeded.Name, live.Name),
        Pick(typed.Auftrag, _seeded.Auftrag, live.Auftrag),
        Pick(typed.Standort, _seeded.Standort, live.Standort),
        Pick(typed.Funkgruppe, _seeded.Funkgruppe, live.Funkgruppe),
        Pick(typed.Rufname, _seeded.Rufname, live.Rufname),
        Pick(typed.Telefon, _seeded.Telefon, live.Telefon));

    private static T Pick<T>(T typed, T seeded, T live) =>
        EqualityComparer<T>.Default.Equals(typed, seeded) ? live : typed;

    // An Unterabschnitt needs its Abschnitt still standing; a peer may have dissolved it meanwhile.
    private bool ParentIsGone() =>
        _kind == CommandNodeKind.Unterabschnitt
        && !_session.Incident.CommandNodes.Any(n => n.Id == _parentId && n.IsLive && n.Kind == CommandNodeKind.Abschnitt);

    /// <summary>ABBRECHEN, and Esc: closes at once when nothing was typed, asks first otherwise.</summary>
    [RelayCommand]
    private void Cancel()
    {
        if (HasChanges)
        {
            IsDiscardConfirmOpen = true;
            return;
        }

        Closed?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ConfirmDiscard()
    {
        IsDiscardConfirmOpen = false;
        Closed?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void CancelDiscard() => IsDiscardConfirmOpen = false;

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private readonly record struct Fields(
        string Name, string? Auftrag, string? Standort, string? Funkgruppe, string? Rufname, string? Telefon);
}
