using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.Files;
using LageBuch.Persistence.MasterData;
using LageBuch.Sync;

namespace LageBuch.AppLogic.Tests;

// The workspace's view of the seal (#598): the read-only banner names it, and a file that no
// longer matches it raises a warning.
public class IncidentWorkspaceSealTests
{
    private const string Path = "/x.fwincident";

    private static readonly DateTimeOffset T0 = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public async Task A_sealed_closed_incident_names_its_seal_in_the_read_only_banner()
    {
        var store = new FakeStore();
        var host = await ClosedWithPhoto(store);

        var vm = Workspace(LocalIncidentSession.OpenReadOnly(store, new FixedClock(T0), Path));
        await vm.SealCheckCompleted;

        Assert.Equal($"Abgeschlossen — schreibgeschützt · Siegel: {host.Incident.Seal!.ShortForm}", vm.ReadOnlyReason);
        Assert.Null(vm.SealWarning);
    }

    [Fact]
    public async Task A_changed_attachment_raises_the_seal_warning_and_the_incident_stays_viewable()
    {
        var store = new FakeStore();
        var host = await ClosedWithPhoto(store);
        var photo = host.Incident.Files[0];
        await File.WriteAllBytesAsync(
            store.ResolveFileDiskPath(Path, IncidentFile.StorageFileName(photo.Id, photo.FileName)),
            "anderes foto"u8.ToArray());

        var vm = Workspace(LocalIncidentSession.OpenReadOnly(store, new FixedClock(T0), Path));
        await vm.SealCheckCompleted;

        Assert.Equal(
            "Siegel verletzt: Anhang verändert: foto.jpg. Die Datei stimmt nicht mehr mit dem versiegelten Stand überein.",
            vm.SealWarning);
        Assert.True(vm.IsReadOnly);
        Assert.Equal("Abgeschlossen", vm.StatusDisplay);
    }

    [Fact]
    public async Task A_file_closed_before_the_seal_says_kein_Siegel_without_a_warning()
    {
        var store = new FakeStore();
        var clock = new FixedClock(T0);
        var op = new SessionOperator("Müller");
        var incident = Incident.Start(clock, op);
        incident.Close(clock, op);
        store.Save(Path, incident);

        var vm = Workspace(LocalIncidentSession.OpenReadOnly(store, clock, Path));
        await vm.SealCheckCompleted;

        Assert.Equal("Abgeschlossen — schreibgeschützt · kein Siegel", vm.ReadOnlyReason);
        Assert.Null(vm.SealWarning);
    }

    [Fact]
    public void Closing_in_the_workspace_shows_the_new_seal_at_once()
    {
        var session = Start(new FakeStore());
        var vm = Workspace(session);
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.CloseIncidentCommand.Execute(null);
        vm.PendingConfirm!.ConfirmCommand.Execute(null);

        Assert.Contains(nameof(IncidentWorkspaceViewModel.ReadOnlyReason), raised);
        Assert.Equal($"Abgeschlossen — schreibgeschützt · Siegel: {session.Incident.Seal!.ShortForm}", vm.ReadOnlyReason);
        Assert.Null(vm.SealWarning);
    }

    // The seal does not travel over the wire yet, so a joined client has nothing to show -- and
    // must not claim "kein Siegel" for an incident the host did seal.
    [Fact]
    public void A_joined_client_shows_no_seal_line_and_no_warning()
    {
        var host = Start(new FakeStore());
        host.Close();

        var vm = Workspace(new SnapshotRoundTrippingSession(host));

        Assert.Equal("Abgeschlossen — schreibgeschützt", vm.ReadOnlyReason);
        Assert.Null(vm.SealWarning);
    }

    private static LocalIncidentSession Start(IIncidentStore store) =>
        TestSession.StartNew(
            store,
            new FixedClock(T0),
            new SessionOperator("Müller"),
            Path,
            new[] { ("A?", false) },
            Array.Empty<(string, bool)>());

    private static async Task<LocalIncidentSession> ClosedWithPhoto(FakeStore store)
    {
        var session = Start(store);
        await session.AddFileAsync("foto.jpg", "image/jpeg", "foto"u8.ToArray());
        session.Close();
        return session;
    }

    private static IncidentWorkspaceViewModel Workspace(IIncidentSession session) =>
        new(
            session,
            new FixedClock(T0),
            new FakeTicker(),
            MasterDataSet.Empty,
            new FakeDialogs(),
            new FakeAlarmService(),
            new NoopIncidentHostController());
}
