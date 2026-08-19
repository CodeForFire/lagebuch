using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Feuerwehr.AppLogic.Services;
using Feuerwehr.Persistence.MasterData;

namespace Feuerwehr.AppLogic.ViewModels;

/// <summary>
/// Quick-access list of the global Links Stammdaten, shown as a read-only tab in the incident
/// workspace so a link can be opened while working an Einsatz. Unlike <see cref="FilesViewModel"/>
/// this holds no <see cref="Feuerwehr.Sync.IIncidentSession"/> and mutates nothing — Links are
/// global master data, not incident state, so opening one is not an incident action.
/// </summary>
public sealed partial class LinksViewModel : ObservableObject
{
    private readonly IFileDialogService _dialogs;

    public LinksViewModel(IReadOnlyList<Link> links, IFileDialogService dialogs)
    {
        _dialogs = dialogs;
        Links = links;
    }

    public IReadOnlyList<Link> Links { get; }

    [RelayCommand]
    private Task OpenAsync(Link link)
    {
        var target = link.Url.Contains("://", StringComparison.Ordinal) ? link.Url : $"https://{link.Url}";
        return _dialogs.OpenUrlAsync(target);
    }
}
