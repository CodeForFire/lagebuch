using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.Atemschutz;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.Tests;

/// <summary>The workspace's side of the Strahlenschutzüberwachung (#994): the dialog overlay and the
/// refused close.</summary>
public sealed class IncidentWorkspaceRadiationTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 19, 22, 17, 0, TimeSpan.FromHours(2));

    private static IncidentWorkspaceViewModel Workspace(out LocalIncidentSession session)
    {
        var clock = new FixedClock(T0);
        session = TestSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Müller"),
            "/x.lagebuch",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        return new IncidentWorkspaceViewModel(
            session,
            clock,
            new FakeTicker(),
            MasterDataSet.Empty with { TruppTypes = new[] { new TruppType("Strahlenschutztrupp", 2, 30, RadiationMonitoring: true) } },
            new FakeDialogs(),
            new FakeAlarmService(),
            new NoopIncidentHostController());
    }

    private static AtemschutzTrupp StartedStrahlenschutztrupp(LocalIncidentSession session)
    {
        session.AddScbaTrupp("Strahlenschutztrupp", TruppMember.Crew("Maier", "Wagner"), 300, callSign: "HLF 1", radiationMonitoring: true);
        var trupp = session.Incident.ScbaTrupps[^1];
        foreach (var m in trupp.Members.ToList())
        {
            session.RecordScbaRadiation(trupp.Id, m.Role, new RadiationRecord(oslDosimeterNumber: "1", alarmDosimeterNumber: "2"));
        }

        session.StartScbaTrupp(trupp.Id);
        return trupp;
    }

    [Fact]
    public void The_row_opens_the_strahlenschutz_dialog_as_an_overlay()
    {
        using var vm = Workspace(out var session);
        StartedStrahlenschutztrupp(session);
        var row = Assert.Single(vm.Scba.Trupps);

        row.OpenRadiationCommand.Execute(null);

        var dialog = Assert.IsType<RadiationDialogViewModel>(vm.PendingRadiationDialog);
        Assert.Equal("Strahlenschutz · HLF 1 · Strahlenschutztrupp", dialog.Title);

        dialog.CancelCommand.Execute(null);
        Assert.Null(vm.PendingRadiationDialog);
    }

    // Refused rather than warned about: a closed Einsatz is read-only, so the reading could never
    // be added afterwards. The way out leads to where it is entered.
    [Fact]
    public void Closing_with_a_missing_reading_is_refused_and_leads_to_the_atemschutzueberwachung()
    {
        using var vm = Workspace(out var session);
        StartedStrahlenschutztrupp(session);

        vm.CloseIncidentCommand.Execute(null);

        var refusal = vm.PendingConfirm;
        Assert.NotNull(refusal);
        Assert.Equal("Einsatz noch nicht abschließbar", refusal.Title);
        Assert.StartsWith("Ablesewerte fehlen: HLF 1 · Strahlenschutztrupp.", refusal.Message, StringComparison.Ordinal);
        Assert.Equal("ZUR ATEMSCHUTZÜBERWACHUNG", refusal.ConfirmLabel);

        refusal.ConfirmCommand.Execute(null);

        Assert.Equal(IncidentState.Open, session.Incident.State);
        Assert.Same(vm.Scba, vm.SelectedNavItem?.Content);
        Assert.Null(vm.PendingConfirm);
    }

    [Fact]
    public void Once_every_reading_is_in_the_einsatz_closes_as_usual()
    {
        using var vm = Workspace(out var session);
        var trupp = StartedStrahlenschutztrupp(session);
        foreach (var m in trupp.Members.ToList())
        {
            session.RecordScbaRadiation(
                trupp.Id, m.Role, new RadiationRecord("1", false, "2", false, alarmReading: new DoseReading(0.01m, DoseUnit.MilliSievert)));
        }

        vm.CloseIncidentCommand.Execute(null);

        Assert.Equal("Einsatz abschließen?", vm.PendingConfirm?.Title);
    }
}
