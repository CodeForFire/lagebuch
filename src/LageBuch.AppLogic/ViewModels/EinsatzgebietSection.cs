using CommunityToolkit.Mvvm.ComponentModel;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// Editor for the Wasserförderung region of operation (#150 phase 2): a name and a folder path
/// expected to hold <c>region.mbtiles</c> and <c>region.dem</c>. Two scalar strings, so this
/// mirrors <see cref="SettingsSection"/>'s pattern rather than the list sections' — one bindable
/// property each, reporting through the shared dirty callback.
/// </summary>
public sealed partial class EinsatzgebietSection : EditorSection
{
    private readonly Action _onChanged;

    public EinsatzgebietSection(string title, Einsatzgebiet einsatzgebiet, Action onChanged) : base(title)
    {
        _onChanged = onChanged;
        _name = einsatzgebiet.Name;
        _folderPath = einsatzgebiet.FolderPath;
    }

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _folderPath = string.Empty;

    partial void OnNameChanged(string value) => _onChanged();
    partial void OnFolderPathChanged(string value) => _onChanged();

    public Einsatzgebiet ToEinsatzgebiet() => new(Name, FolderPath);
}
