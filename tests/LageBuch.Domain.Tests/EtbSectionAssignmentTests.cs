using LageBuch.Domain.Etb;
using LageBuch.Domain.Leadership;

namespace LageBuch.Domain.Tests;

// An ETB entry assigned to an Einsatzabschnitt (#971): by id for the filter, by value for the
// record, so the entry still reads as it did once the Abschnitt is renamed or dissolved.
public class EtbSectionAssignmentTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 27, 14, 0, 0, TimeSpan.FromHours(2));

    private static (Incident Incident, FixedClock Clock, SessionOperator Op) NewIncident()
    {
        var clock = new FixedClock(T0);
        var op = new SessionOperator("Muster", "FFB 12/1");
        return (Incident.Start(clock, op), clock, op);
    }

    [Fact]
    public void A_new_entry_takes_the_abschnitt_by_id_and_by_name()
    {
        var (incident, clock, op) = NewIncident();
        var nord = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");

        var entry = incident.AddJournalEntry(clock, op, EtbDirection.Internal, "Lage Nord", sectionId: nord.Id);

        Assert.Equal(nord.Id, entry.SectionId);
        Assert.Equal("Nord", entry.SectionName);
    }

    [Fact]
    public void An_unterabschnitt_is_stored_with_its_abschnitt_in_the_name()
    {
        var (incident, clock, op) = NewIncident();
        var nord = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");
        var zugang = incident.AddCommandNode(clock, op, CommandNodeKind.Unterabschnitt, "Zugang", nord.Id);

        var entry = incident.AddJournalEntry(clock, op, EtbDirection.Internal, "Tür offen", sectionId: zugang.Id);

        Assert.Equal(zugang.Id, entry.SectionId);
        Assert.Equal("Nord / Zugang", entry.SectionName);
    }

    [Fact]
    public void An_entry_without_an_abschnitt_stays_possible()
    {
        var (incident, clock, op) = NewIncident();
        incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");

        var entry = incident.AddJournalEntry(clock, op, EtbDirection.Internal, "Lage allgemein");

        Assert.Null(entry.SectionId);
        Assert.Null(entry.SectionName);
    }

    [Theory]
    [InlineData(CommandNodeKind.Bereitstellung)]
    [InlineData(CommandNodeKind.Partner)]
    public void A_node_that_is_no_einsatzabschnitt_is_refused(CommandNodeKind kind)
    {
        var (incident, clock, op) = NewIncident();
        var node = incident.AddCommandNode(clock, op, kind, "Marktplatz");
        var before = incident.Journal.Count;

        var error = Assert.Throws<InvalidOperationException>(
            () => incident.AddJournalEntry(clock, op, EtbDirection.Internal, "Text", sectionId: node.Id));

        Assert.Equal(Incident.OnlyEinsatzabschnitteForEntries, error.Message);
        Assert.Equal(before, incident.Journal.Count);
    }

    [Fact]
    public void A_dissolved_abschnitt_is_refused()
    {
        var (incident, clock, op) = NewIncident();
        var nord = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");
        incident.DissolveCommandNode(clock, op, nord.Id);

        var error = Assert.Throws<InvalidOperationException>(
            () => incident.AddJournalEntry(clock, op, EtbDirection.Internal, "Text", sectionId: nord.Id));

        Assert.Equal("Abschnitt ist aufgelöst.", error.Message);
    }

    [Fact]
    public void An_unknown_abschnitt_is_refused()
    {
        var (incident, clock, op) = NewIncident();

        Assert.Throws<KeyNotFoundException>(
            () => incident.AddJournalEntry(clock, op, EtbDirection.Internal, "Text", sectionId: Guid.NewGuid()));
    }

    [Fact]
    public void An_app_written_entry_takes_no_abschnitt()
    {
        var (incident, clock, op) = NewIncident();
        var nord = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");

        Assert.Throws<InvalidOperationException>(
            () => incident.AddJournalEntry(clock, op, EtbDirection.System, "Text", sectionId: nord.Id));
    }

    [Fact]
    public void Assigning_an_abschnitt_later_sets_it_and_writes_a_system_line()
    {
        var (incident, clock, op) = NewIncident();
        var nord = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");
        var entry = incident.AddJournalEntry(clock, op, EtbDirection.Internal, "Lage Nord");
        clock.Now = T0.AddMinutes(7);

        var assigned = incident.AssignJournalEntrySection(clock, op, entry.Id, nord.Id);

        Assert.Equal(nord.Id, assigned.SectionId);
        Assert.Equal("Nord", assigned.SectionName);
        Assert.Equal(assigned, incident.Journal.Single(e => e.Id == entry.Id));
        var trace = incident.Journal[^1];
        Assert.Equal(EtbDirection.System, trace.Direction);
        Assert.Equal("ETB-Eintrag 14:00 → Nord", trace.Text);
        Assert.Equal(T0.AddMinutes(7), trace.Timestamp);
    }

    [Fact]
    public void Changing_the_abschnitt_replaces_id_and_name()
    {
        var (incident, clock, op) = NewIncident();
        var nord = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");
        var sued = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Süd");
        var entry = incident.AddJournalEntry(clock, op, EtbDirection.Internal, "Lage", sectionId: nord.Id);

        var moved = incident.AssignJournalEntrySection(clock, op, entry.Id, sued.Id);

        Assert.Equal(sued.Id, moved.SectionId);
        Assert.Equal("Süd", moved.SectionName);
        Assert.Equal("ETB-Eintrag 14:00 → Süd", incident.Journal[^1].Text);
    }

    [Fact]
    public void Removing_the_abschnitt_clears_it_and_says_which_it_was()
    {
        var (incident, clock, op) = NewIncident();
        var nord = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");
        var entry = incident.AddJournalEntry(clock, op, EtbDirection.Internal, "Lage", sectionId: nord.Id);

        var cleared = incident.AssignJournalEntrySection(clock, op, entry.Id, null);

        Assert.Null(cleared.SectionId);
        Assert.Null(cleared.SectionName);
        Assert.Equal("ETB-Eintrag 14:00: Abschnitt aufgehoben (vorher Nord)", incident.Journal[^1].Text);
    }

    [Fact]
    public void Assigning_the_abschnitt_it_already_has_writes_nothing()
    {
        var (incident, clock, op) = NewIncident();
        var nord = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");
        var entry = incident.AddJournalEntry(clock, op, EtbDirection.Internal, "Lage", sectionId: nord.Id);
        var unassigned = incident.AddJournalEntry(clock, op, EtbDirection.Internal, "Lage allgemein");
        var before = incident.Journal.Count;

        Assert.Same(entry, incident.AssignJournalEntrySection(clock, op, entry.Id, nord.Id));
        Assert.Same(unassigned, incident.AssignJournalEntrySection(clock, op, unassigned.Id, null));
        Assert.Equal(before, incident.Journal.Count);
    }

    [Fact]
    public void An_app_written_entry_cannot_be_assigned()
    {
        var (incident, clock, op) = NewIncident();
        var nord = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");
        var system = incident.Journal[^1];
        Assert.True(EtbDirections.IsAppWritten(system.Direction));

        Assert.Throws<InvalidOperationException>(
            () => incident.AssignJournalEntrySection(clock, op, system.Id, nord.Id));
    }

    [Fact]
    public void Assigning_refuses_a_bereitstellung_and_an_unknown_entry()
    {
        var (incident, clock, op) = NewIncident();
        var markt = incident.AddCommandNode(clock, op, CommandNodeKind.Bereitstellung, "Marktplatz");
        var entry = incident.AddJournalEntry(clock, op, EtbDirection.Internal, "Lage");
        var before = incident.Journal.Count;

        Assert.Throws<InvalidOperationException>(() => incident.AssignJournalEntrySection(clock, op, entry.Id, markt.Id));
        Assert.Throws<KeyNotFoundException>(() => incident.AssignJournalEntrySection(clock, op, Guid.NewGuid(), null));
        Assert.Null(incident.Journal.Single(e => e.Id == entry.Id).SectionId);
        Assert.Equal(before, incident.Journal.Count);
    }

    [Fact]
    public void Renaming_or_dissolving_the_abschnitt_keeps_the_stored_name()
    {
        var (incident, clock, op) = NewIncident();
        var nord = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");
        var entry = incident.AddJournalEntry(clock, op, EtbDirection.Internal, "Lage", sectionId: nord.Id);

        incident.UpdateCommandNode(clock, op, nord.Id, "Nordost", null, null, null, null, null);
        incident.DissolveCommandNode(clock, op, nord.Id);

        var stored = incident.Journal.Single(e => e.Id == entry.Id);
        Assert.Equal(nord.Id, stored.SectionId);
        Assert.Equal("Nord", stored.SectionName);
    }

    [Fact]
    public void An_edit_of_the_text_keeps_the_abschnitt()
    {
        var (incident, clock, op) = NewIncident();
        var nord = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");
        var entry = incident.AddJournalEntry(clock, op, EtbDirection.Internal, "Lage", sectionId: nord.Id);

        var edited = incident.EditJournalEntry(clock, op, entry.Id, "Lage korrigiert");

        Assert.Equal(nord.Id, edited.SectionId);
        Assert.Equal("Nord", edited.SectionName);
    }

    [Fact]
    public void Assigning_on_a_closed_incident_is_refused()
    {
        var (incident, clock, op) = NewIncident();
        var entry = incident.AddJournalEntry(clock, op, EtbDirection.Internal, "Lage");
        incident.Close(clock, op);

        Assert.Throws<IncidentClosedException>(() => incident.AssignJournalEntrySection(clock, op, entry.Id, null));
    }

    [Fact]
    public void Rehydrate_carries_the_abschnitt_back()
    {
        var id = Guid.NewGuid();
        var sectionId = Guid.NewGuid();

        var entry = EtbEntry.Rehydrate(id, T0, EtbDirection.Internal, "Lage", "Muster", null, null, null, sectionId, "Nord");

        Assert.Equal(sectionId, entry.SectionId);
        Assert.Equal("Nord", entry.SectionName);
    }

    [Fact]
    public void A_name_without_an_abschnitt_is_not_kept()
    {
        var entry = EtbEntry.Rehydrate(Guid.NewGuid(), T0, EtbDirection.Internal, "Lage", "Muster", null, null, null, null, "Nord");

        Assert.Null(entry.SectionName);
        Assert.Null(entry.WithSection(Guid.NewGuid(), "Süd").WithSection(null, "Süd").SectionName);
    }
}
