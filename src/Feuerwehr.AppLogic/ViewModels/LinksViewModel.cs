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

    /// <summary>
    /// Refuses anything but http(s) before it reaches the OS: on desktop, OpenUrlAsync ultimately
    /// runs Process.Start with UseShellExecute=true, which resolves arbitrary URI handlers and even
    /// local executable paths, and a Link's URL can come from an imported Stammdaten JSON file, not
    /// just what the user themselves typed here.
    /// </summary>
    [RelayCommand]
    private Task OpenAsync(Link link)
    {
        var candidate = link.Url.Contains("://", StringComparison.Ordinal) ? link.Url : $"https://{link.Url}";
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return Task.CompletedTask;

        return _dialogs.OpenUrlAsync(uri.AbsoluteUri);
    }
}
