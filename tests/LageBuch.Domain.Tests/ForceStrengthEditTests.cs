using LageBuch.Domain.Etb;

namespace LageBuch.Domain.Tests;

// #76: the three Stärke-Zahlen of an entered unit are corrigible after the fact — and every real
// change is protokolliert twice, exactly as the user asked: a Systemmeldung in the ETB (like a
// status transition) and a Wert-Historie on the unit itself (like an edited ETB entry, #73).
public class ForceStrengthEditTests
{
    private static readonly DateTimeOffset T0 =
        new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));

    private static Incident NewIncident(out FixedClock clock, out SessionOperator op)
    {
        clock = new FixedClock(T0);
        op = new SessionOperator("Müller", "FFB 12/1");
        return Incident.Start(clock, op);
    }

    [Fact]
    public void Changing_the_strength_replaces_the_unit_in_place()
    {
        var incident = NewIncident(out var clock, out var op);
        var unit = incident.AddForceUnit(clock, op, "FFB Wache 1", 6);

        var updated = incident.UpdateForceStrength(clock, op, unit.Id, officerCount: 1, personnelCount: 9, scbaCount: 4);

        Assert.Equal(unit.Id, updated.Id);
        Assert.Same(updated, Assert.Single(incident.Forces));
        Assert.Equal((1, 9, 4), (updated.OfficerCount, updated.PersonnelCount, updated.ScbaCount));
        Assert.Equal("1/8/9", updated.StrengthText);

        // Status/Bemerkung ride along untouched — only the counts were corrected.
        Assert.Null(updated.Status);
    }

    [Fact]
    public void A_strength_change_logs_a_system_entry_with_old_and_new_format()
    {
        var incident = NewIncident(out var clock, out var op);
        var unit = incident.AddForceUnit(clock, op, "FFB Wache 1", 6, "FFB 1/40/1");
        var before = incident.Journal.Count;

        incident.UpdateForceStrength(clock, op, unit.Id, officerCount: 1, personnelCount: 9, scbaCount: 4);

        var entry = Assert.Single(incident.Journal.Skip(before));
        Assert.Equal(EtbDirection.System, entry.Direction);
        Assert.Equal("FFB Wache 1 (FFB 1/40/1): Stärke 0/6/6 → 1/8/9, davon AGT 0 → 4", entry.Text);

        // Same from-call-sign convention as the status transition entry (UpdateForceUnit).
        Assert.Equal("FFB 1/40/1", entry.From);
    }

    [Fact]
    public void An_unchanged_resubmission_is_not_a_change()
    {
        var incident = NewIncident(out var clock, out var op);
        var unit = incident.AddForceUnit(clock, op, "Aich", 6);
        var before = incident.Journal.Count;

        var updated = incident.UpdateForceStrength(clock, op, unit.Id, officerCount: 0, personnelCount: 6, scbaCount: 0);

        Assert.Equal(before, incident.Journal.Count);
        Assert.Empty(updated.Edits);
    }

    [Fact]
    public void Every_real_change_records_who_changed_what_and_when()
    {
        var incident = NewIncident(out var clock, out var op);
        var unit = incident.AddForceUnit(clock, op, "Aich", 6);

        var first = incident.UpdateForceStrength(clock, op, unit.Id, officerCount: 1, personnelCount: 7, scbaCount: 2);
        clock.Now = T0.AddMinutes(5);
        var second = incident.UpdateForceStrength(clock, op, unit.Id, officerCount: 1, personnelCount: 8, scbaCount: 3);

        // Each edit retains the state *before that edit*, so the history chains back to entry.
        var firstEdit = Assert.Single(first.Edits);
        Assert.Equal((0, 6, 0), (firstEdit.PreviousOfficerCount, firstEdit.PreviousPersonnelCount, firstEdit.PreviousScbaCount));
        Assert.Equal("Müller (FFB 12/1)", firstEdit.EditedBy);
        Assert.Equal(T0, firstEdit.EditedAt);

        // The history accumulates: the second correction sees both prior states.
        Assert.Equal(2, second.Edits.Count);
        var secondEdit = second.Edits.Last();
        Assert.Equal((1, 7, 2), (secondEdit.PreviousOfficerCount, secondEdit.PreviousPersonnelCount, secondEdit.PreviousScbaCount));
        Assert.Equal(T0.AddMinutes(5), secondEdit.EditedAt);
    }

    [Fact]
    public void Strength_rules_still_hold_on_correction()
    {
        var incident = NewIncident(out var clock, out var op);
        var unit = incident.AddForceUnit(clock, op, "Aich", 6);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => incident.UpdateForceStrength(clock, op, unit.Id, officerCount: 7, personnelCount: 6, scbaCount: 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => incident.UpdateForceStrength(clock, op, unit.Id, officerCount: 0, personnelCount: 6, scbaCount: 7));
    }

    [Fact]
    public void Unknown_unit_throws_and_closed_incidents_refuse_corrections()
    {
        var incident = NewIncident(out var clock, out var op);

        Assert.Throws<KeyNotFoundException>(
            () => incident.UpdateForceStrength(clock, op, Guid.NewGuid(), 0, 6, 0));

        incident.Close(clock, op);
        var open = NewIncident(out _, out _);
        var unit = open.AddForceUnit(clock, op, "Aich", 6);
        open.Close(clock, op);
        Assert.Throws<IncidentClosedException>(
            () => open.UpdateForceStrength(clock, op, unit.Id, 0, 7, 0));
    }
}
