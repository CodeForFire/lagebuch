using Feuerwehr.Domain.Etb;

namespace Feuerwehr.Domain.Tests;

public class EtbEntryTests
{
    private static readonly DateTimeOffset At =
        new(2026, 6, 22, 10, 30, 0, TimeSpan.FromHours(2));

    [Fact]
    public void Create_populates_fields_and_attributes_operator()
    {
        var op = new SessionOperator("Müller", "FFB 12/1");

        var entry = EtbEntry.Create(At, EtbDirection.Incoming, "Lagemeldung erhalten", op, from: "ILS");

        Assert.NotEqual(Guid.Empty, entry.Id);
        Assert.Equal(At, entry.Timestamp);
        Assert.Equal(EtbDirection.Incoming, entry.Direction);
        Assert.Equal("Lagemeldung erhalten", entry.Text);
        Assert.Equal("ILS", entry.From);
        Assert.Equal("Müller (FFB 12/1)", entry.EnteredBy);
    }

    [Fact]
    public void Create_rejects_blank_text()
    {
        var op = new SessionOperator("Müller");
        Assert.Throws<ArgumentException>(() => EtbEntry.Create(At, EtbDirection.Internal, "  ", op));
    }

    [Fact]
    public void Each_entry_gets_a_unique_id()
    {
        var op = new SessionOperator("Müller");
        var a = EtbEntry.Create(At, EtbDirection.Internal, "a", op);
        var b = EtbEntry.Create(At, EtbDirection.Internal, "b", op);
        Assert.NotEqual(b.Id, a.Id);
    }

    [Fact]
    public void WithEditedText_replaces_the_text_and_records_the_prior_version()
    {
        var author = new SessionOperator("Müller");
        var entry = EtbEntry.Create(At, EtbDirection.Incoming, "Lagemeldung erhalten", author, from: "ILS");
        var editor = new SessionOperator("Schmidt");
        var editedAt = At.AddMinutes(5);

        var edited = entry.WithEditedText("Lagemeldung korrigiert", editor, editedAt);

        Assert.Equal("Lagemeldung korrigiert", edited.Text);
        var edit = Assert.Single(edited.Edits);
        Assert.Equal("Lagemeldung erhalten", edit.PreviousText);
        Assert.Equal("Schmidt", edit.EditedBy);
        Assert.Equal(editedAt, edit.EditedAt);
    }

    [Fact]
    public void WithEditedText_keeps_id_timestamp_direction_from_and_to_unchanged()
    {
        var op = new SessionOperator("Müller");
        var entry = EtbEntry.Create(At, EtbDirection.Outgoing, "Original", op, from: "Land 1", to: "Leitstelle");

        var edited = entry.WithEditedText("Korrigiert", op, At.AddMinutes(1));

        Assert.Equal(entry.Id, edited.Id);
        Assert.Equal(entry.Timestamp, edited.Timestamp);
        Assert.Equal(entry.Direction, edited.Direction);
        Assert.Equal("Land 1", edited.From);
        Assert.Equal("Leitstelle", edited.To);
    }

    [Fact]
    public void WithEditedText_rejects_blank_text()
    {
        var op = new SessionOperator("Müller");
        var entry = EtbEntry.Create(At, EtbDirection.Internal, "Original", op);
        Assert.Throws<ArgumentException>(() => entry.WithEditedText("   ", op, At.AddMinutes(1)));
    }

    [Fact]
    public void Sequential_edits_accumulate_history_in_order()
    {
        var op = new SessionOperator("Müller");
        var entry = EtbEntry.Create(At, EtbDirection.Internal, "v1", op);

        var afterFirst = entry.WithEditedText("v2", op, At.AddMinutes(1));
        var afterSecond = afterFirst.WithEditedText("v3", op, At.AddMinutes(2));

        Assert.Equal("v3", afterSecond.Text);
        Assert.Equal(2, afterSecond.Edits.Count);
        Assert.Equal("v1", afterSecond.Edits[0].PreviousText);
        Assert.Equal("v2", afterSecond.Edits[1].PreviousText);
    }
}
