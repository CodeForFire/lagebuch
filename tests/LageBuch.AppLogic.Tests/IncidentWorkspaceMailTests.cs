using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.Tests;

// Closing an incident can go straight on to the final PDF (#425) and, optionally, a prefilled e-mail.
public sealed class IncidentWorkspaceMailTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 19, 22, 17, 0, TimeSpan.FromHours(2));

    private readonly string _exportPath = Path.Join(Path.GetTempPath(), $"mail-export-{Guid.NewGuid():N}.pdf");

    public void Dispose() => File.Delete(_exportPath);

    private static IncidentWorkspaceViewModel Workspace(
        FakeDialogs? dialogs = null,
        IIncidentPdfExporter? exporter = null,
        IMailComposer? mail = null,
        ILastPdfExportStore? lastPdfExport = null)
    {
        var clock = new FixedClock(T0);
        var session = TestSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Müller"),
            "/x.fwincident",
            new[] { ("A?", false) },
            Array.Empty<(string, bool)>());
        session.Incident.SetKeyword("B3 Wohnungsbrand");
        return new IncidentWorkspaceViewModel(
            session,
            clock,
            new FakeTicker(),
            MasterDataSet.Empty,
            dialogs ?? new FakeDialogs(),
            new FakeAlarmService(),
            new NoopIncidentHostController(),
            exporter ?? new TestPdfExporter(),
            lastPdfExport,
            mailComposer: mail);
    }

    // Mailing is nested under exporting in the close dialog, so mail implies export.
    private static void CloseWith(IncidentWorkspaceViewModel vm, bool export, bool mail = false)
    {
        vm.CloseIncidentCommand.Execute(null);
        var confirm = vm.PendingConfirm;
        Assert.NotNull(confirm);
        confirm.IsOptionChecked = export || mail;
        confirm.IsSubOptionChecked = mail;
        confirm.ConfirmCommand.Execute(null);
    }

    private static void CloseWithMail(IncidentWorkspaceViewModel vm, bool mail) => CloseWith(vm, export: mail, mail);

    [Fact]
    public void Close_dialog_offers_the_pdf_export_with_mailing_nested_under_it()
    {
        var vm = Workspace(mail: new FakeMailComposer());

        vm.CloseIncidentCommand.Execute(null);

        Assert.True(vm.PendingConfirm?.HasOption);
        Assert.Equal("PDF exportieren", vm.PendingConfirm?.OptionLabel);
        Assert.True(vm.PendingConfirm?.HasSubOption);
        Assert.Equal("und per E-Mail senden", vm.PendingConfirm?.SubOptionLabel);
    }

    [Fact]
    public void Close_dialog_offers_nothing_without_a_pdf_exporter()
    {
        var vm = Workspace(exporter: new NoopIncidentPdfExporter(), mail: new FakeMailComposer());

        vm.CloseIncidentCommand.Execute(null);

        Assert.False(vm.PendingConfirm?.HasOption);
        Assert.False(vm.PendingConfirm?.HasSubOption);
    }

    [Fact]
    public void Close_dialog_offers_the_export_but_no_mail_without_a_mail_composer()
    {
        var vm = Workspace();

        vm.CloseIncidentCommand.Execute(null);

        Assert.True(vm.PendingConfirm?.HasOption);
        Assert.Equal("PDF exportieren", vm.PendingConfirm?.OptionLabel);
        Assert.False(vm.PendingConfirm?.HasSubOption);
    }

    [Fact]
    public void Closing_with_the_export_option_opens_the_pdf_section_dialog_after_closing()
    {
        var vm = Workspace();

        CloseWith(vm, export: true);

        Assert.True(vm.IsReadOnly);
        Assert.Null(vm.PendingConfirm);
        Assert.Equal("EXPORTIEREN", vm.PendingPdfExportOptions?.ExportLabel);
    }

    [Fact]
    public async Task Closing_with_the_export_option_writes_the_pdf_and_records_the_last_export()
    {
        var store = new FakeLastPdfExportStore();
        var vm = Workspace(new FakeDialogs { ExportPath = _exportPath }, lastPdfExport: store);
        CloseWith(vm, export: true);

        await vm.PendingPdfExportOptions!.ExportCommand.ExecuteAsync(null);

        Assert.True(File.Exists(_exportPath));
        Assert.Equal($"PDF exportiert: {Path.GetFileName(_exportPath)}", vm.ExportStatus);
        Assert.Equal(_exportPath, store.SetPath);
        Assert.Null(vm.PendingPdfExportOptions);
    }

    [Fact]
    public async Task Closing_with_export_but_not_mail_composes_no_mail()
    {
        var mail = new FakeMailComposer();
        var vm = Workspace(new FakeDialogs { ExportPath = _exportPath }, mail: mail);
        CloseWith(vm, export: true, mail: false);

        await vm.PendingPdfExportOptions!.ExportCommand.ExecuteAsync(null);

        Assert.True(File.Exists(_exportPath));
        Assert.Empty(mail.Drafts);
    }

    [Fact]
    public void Closing_with_the_mail_option_opens_the_pdf_section_dialog_after_closing()
    {
        var vm = Workspace(mail: new FakeMailComposer());

        CloseWithMail(vm, mail: true);

        Assert.True(vm.IsReadOnly);
        Assert.Null(vm.PendingConfirm);
        Assert.NotNull(vm.PendingPdfExportOptions);
        Assert.Equal("PDF ERSTELLEN & MAILEN", vm.PendingPdfExportOptions.ExportLabel);
    }

    [Fact]
    public void Closing_without_any_option_opens_no_export_dialog()
    {
        var vm = Workspace(mail: new FakeMailComposer());

        CloseWith(vm, export: false);

        Assert.True(vm.IsReadOnly);
        Assert.Null(vm.PendingPdfExportOptions);
    }

    [Fact]
    public async Task Mail_export_hands_the_written_pdf_and_incident_subject_to_the_composer()
    {
        var mail = new FakeMailComposer();
        var vm = Workspace(new FakeDialogs { ExportPath = _exportPath }, mail: mail);
        CloseWithMail(vm, mail: true);

        await vm.PendingPdfExportOptions!.ExportCommand.ExecuteAsync(null);

        Assert.True(File.Exists(_exportPath));
        var draft = Assert.Single(mail.Drafts);
        Assert.Equal(_exportPath, draft.AttachmentPath);
        Assert.Equal("Einsatzbericht B3 Wohnungsbrand · 19.09.2026 22:17", draft.Subject);
        Assert.Contains("Abschluss:", draft.Body, StringComparison.Ordinal);
        Assert.Equal($"PDF per E-Mail vorbereitet: {Path.GetFileName(_exportPath)}", vm.ExportStatus);
        Assert.Null(vm.PendingPdfExportOptions);
    }

    [Fact]
    public async Task Cancelled_save_dialog_composes_no_mail()
    {
        var mail = new FakeMailComposer();
        var vm = Workspace(new FakeDialogs { ExportPath = null }, mail: mail);
        CloseWithMail(vm, mail: true);

        await vm.PendingPdfExportOptions!.ExportCommand.ExecuteAsync(null);

        Assert.Empty(mail.Drafts);
    }

    [Fact]
    public async Task A_failed_export_composes_no_mail()
    {
        var mail = new FakeMailComposer();
        var vm = Workspace(new FakeDialogs { ExportPath = _exportPath }, new ThrowingPdfExporter(), mail);
        CloseWithMail(vm, mail: true);

        await vm.PendingPdfExportOptions!.ExportCommand.ExecuteAsync(null);

        Assert.Empty(mail.Drafts);
        Assert.StartsWith("Export fehlgeschlagen", vm.ExportStatus, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Mail_fallback_tells_the_lagebuchfuehrer_to_attach_the_pdf()
    {
        var mail = new FakeMailComposer { Result = MailComposeResult.OpenedWithoutAttachment };
        var vm = Workspace(new FakeDialogs { ExportPath = _exportPath }, mail: mail);
        CloseWithMail(vm, mail: true);

        await vm.PendingPdfExportOptions!.ExportCommand.ExecuteAsync(null);

        Assert.Equal($"E-Mail geöffnet – PDF bitte anhängen: {Path.GetFileName(_exportPath)}", vm.ExportStatus);
    }

    [Fact]
    public async Task Failed_compose_still_reports_the_exported_pdf()
    {
        var mail = new FakeMailComposer { Result = MailComposeResult.Failed };
        var vm = Workspace(new FakeDialogs { ExportPath = _exportPath }, mail: mail);
        CloseWithMail(vm, mail: true);

        await vm.PendingPdfExportOptions!.ExportCommand.ExecuteAsync(null);

        Assert.Equal(
            $"PDF exportiert: {Path.GetFileName(_exportPath)} – E-Mail-Programm nicht gefunden",
            vm.ExportStatus);
        Assert.Equal(_exportPath, vm.ExportStatusDetail);
    }

    [Fact]
    public async Task A_throwing_composer_surfaces_in_the_status_line()
    {
        var mail = new FakeMailComposer { Failure = new InvalidOperationException("kaputt") };
        var vm = Workspace(new FakeDialogs { ExportPath = _exportPath }, mail: mail);
        CloseWithMail(vm, mail: true);

        await vm.PendingPdfExportOptions!.ExportCommand.ExecuteAsync(null);

        Assert.Equal(
            $"PDF exportiert: {Path.GetFileName(_exportPath)} – E-Mail fehlgeschlagen: kaputt",
            vm.ExportStatus);
    }

    [Fact]
    public void The_plain_pdf_export_keeps_its_own_button_label()
    {
        var vm = Workspace(mail: new FakeMailComposer());

        vm.ExportPdfCommand.Execute(null);

        Assert.Equal("EXPORTIEREN", vm.PendingPdfExportOptions?.ExportLabel);
    }
}

internal sealed class FakeMailComposer : IMailComposer
{
    public List<MailDraft> Drafts { get; } = new();

    public MailComposeResult Result { get; set; } = MailComposeResult.Attached;

    public Exception? Failure { get; set; }

    public bool CanCompose => true;

    public Task<MailComposeResult> ComposeAsync(MailDraft draft, CancellationToken cancellationToken = default)
    {
        Drafts.Add(draft);
        return Failure is null ? Task.FromResult(Result) : Task.FromException<MailComposeResult>(Failure);
    }
}
