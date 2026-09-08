using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.Domain;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// Section-selection overlay shown before a PDF export (#262): lets the operator uncheck any of
/// the 8 report sections rather than always rendering everything. "Exportieren" stays open with a
/// busy banner (mirroring <see cref="OperatorPromptViewModel.IsBusy"/>) while the host's callback
/// runs (dialog picker + generation + write + share), then raises <see cref="Closed"/> so the host
/// removes the overlay regardless of outcome.
/// </summary>
public sealed partial class PdfExportOptionsViewModel : ObservableObject
{
    private readonly Func<IncidentPdfSections, Task> _onExport;

    public PdfExportOptionsViewModel(Func<IncidentPdfSections, Task> onExport)
    {
        _onExport = onExport;
        Items = new[]
        {
            new PdfSectionOptionViewModel(IncidentPdfSections.Checklist, "Checkliste", NotifyCanExecuteChanged),
            new PdfSectionOptionViewModel(IncidentPdfSections.Etb, "Einsatztagebuch (ETB)", NotifyCanExecuteChanged),
            new PdfSectionOptionViewModel(IncidentPdfSections.Roles, "Funktionszuweisung", NotifyCanExecuteChanged),
            new PdfSectionOptionViewModel(IncidentPdfSections.Forces, "Kräfteübersicht", NotifyCanExecuteChanged),
            new PdfSectionOptionViewModel(IncidentPdfSections.Tasks, "Aufgaben", NotifyCanExecuteChanged),
            new PdfSectionOptionViewModel(IncidentPdfSections.Atemschutz, "Atemschutzüberwachung", NotifyCanExecuteChanged),
            new PdfSectionOptionViewModel(IncidentPdfSections.CoMessprotokoll, "CO-Messprotokoll", NotifyCanExecuteChanged),
            new PdfSectionOptionViewModel(IncidentPdfSections.Files, "Angehängte Dateien", NotifyCanExecuteChanged),
        };
    }

    public IReadOnlyList<PdfSectionOptionViewModel> Items { get; }

    /// <summary>Raised after Export completes or Cancel, so the host removes the overlay.</summary>
    public event EventHandler? Closed;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isBusy;

    private bool CanExport => !IsBusy && Items.Any(i => i.IsSelected);

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task Export()
    {
        var sections = Items.Where(i => i.IsSelected).Aggregate(IncidentPdfSections.None, (acc, i) => acc | i.Section);
        IsBusy = true;
        try
        {
            await _onExport(sections);
        }
        finally
        {
            IsBusy = false;
        }

        Closed?.Invoke(this, EventArgs.Empty);
    }

    private bool CanCancel => !IsBusy;

    // The IsBusy check is repeated inside the command body, not just in CanCancel: a generated
    // RelayCommand's Execute() does not itself re-check CanExecute, so a caller that bypasses the
    // binding (e.g. a view's own KeyDown handler calling .Execute(null) directly) must not be able
    // to force-close the dialog while its export Task is still running in the background.
    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        if (IsBusy)
        {
            return;
        }

        Closed?.Invoke(this, EventArgs.Empty);
    }

    private void NotifyCanExecuteChanged() => ExportCommand.NotifyCanExecuteChanged();
}

public sealed partial class PdfSectionOptionViewModel : ObservableObject
{
    private readonly Action _onChanged;

    public PdfSectionOptionViewModel(IncidentPdfSections section, string label, Action onChanged)
    {
        Section = section;
        Label = label;
        _onChanged = onChanged;
    }

    public IncidentPdfSections Section { get; }

    public string Label { get; }

    [ObservableProperty]
    private bool _isSelected = true;

    partial void OnIsSelectedChanged(bool value) => _onChanged();
}
