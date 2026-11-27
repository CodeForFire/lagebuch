using LageBuch.Domain.Etb;

namespace LageBuch.Domain.Tests;

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
    public void Create_rejects_text_longer_than_the_limit()
    {
        var op = new SessionOperator("Müller");
        var tooLong = new string('x', EtbEntry.MaxTextLength + 1);
        Assert.Throws<ArgumentException>(() => EtbEntry.Create(At, EtbDirection.Internal, tooLong, op));
    }

    [Fact]
    public void Create_accepts_text_at_exactly_the_limit()
    {
        var op = new SessionOperator("Müller");
        var atLimit = new string('x', EtbEntry.MaxTextLength);
        var entry = EtbEntry.Create(At, EtbDirection.Internal, atLimit, op);
        Assert.Equal(EtbEntry.MaxTextLength, entry.Text.Length);
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
    public void WithEdit_replaces_the_text_and_records_the_prior_version()
    {
        var author = new SessionOperator("Müller");
        var entry = EtbEntry.Create(At, EtbDirection.Incoming, "Lagemeldung erhalten", author, from: "ILS");
        var editor = new SessionOperator("Schmidt");
        var editedAt = At.AddMinutes(5);

        var edited = entry.WithEdit("Lagemeldung korrigiert", entry.From, entry.To, editor, editedAt);

        Assert.Equal("Lagemeldung korrigiert", edited.Text);
        var edit = Assert.Single(edited.Edits);
        Assert.Equal("Lagemeldung erhalten", edit.PreviousText);
        Assert.Equal("Schmidt", edit.EditedBy);
        Assert.Equal(editedAt, edit.EditedAt);
    }

    [Fact]
    public void WithEdit_keeps_id_timestamp_direction_from_and_to_unchanged()
    {
        var op = new SessionOperator("Müller");
        var entry = EtbEntry.Create(At, EtbDirection.Outgoing, "Original", op, from: "Land 1", to: "Leitstelle");

        var edited = entry.WithEdit("Korrigiert", entry.From, entry.To, op, At.AddMinutes(1));

        Assert.Equal(entry.Id, edited.Id);
        Assert.Equal(entry.Timestamp, edited.Timestamp);
        Assert.Equal(entry.Direction, edited.Direction);
        Assert.Equal("Land 1", edited.From);
        Assert.Equal("Leitstelle", edited.To);
    }

    [Fact]
    public void WithEdit_rejects_blank_text()
    {
        var op = new SessionOperator("Müller");
        var entry = EtbEntry.Create(At, EtbDirection.Internal, "Original", op);
        Assert.Throws<ArgumentException>(() => entry.WithEdit("   ", entry.From, entry.To, op, At.AddMinutes(1)));
    }

    [Fact]
    public void WithEdit_rejects_text_longer_than_the_limit()
    {
        var op = new SessionOperator("Müller");
        var entry = EtbEntry.Create(At, EtbDirection.Internal, "Original", op);
        var tooLong = new string('x', EtbEntry.MaxTextLength + 1);
        Assert.Throws<ArgumentException>(() => entry.WithEdit(tooLong, entry.From, entry.To, op, At.AddMinutes(1)));
    }

    /// <summary>
    /// Resubmitting the same text is not an edit -- it must not grow Edits, both because it isn't a
    /// real correction and because otherwise a flood of no-op "edits" could inflate the retained
    /// history without bound (security review, #73).
    /// </summary>
    [Fact]
    public void WithEdit_is_a_no_op_when_the_text_is_unchanged()
    {
        var op = new SessionOperator("Müller");
        var entry = EtbEntry.Create(At, EtbDirection.Internal, "Original", op);

        var result = entry.WithEdit("Original", entry.From, entry.To, op, At.AddMinutes(1));

        Assert.Same(entry, result);
        Assert.Empty(result.Edits);
    }

    [Fact]
    public void WithEdit_is_a_no_op_when_the_text_differs_only_by_surrounding_whitespace()
    {
        var op = new SessionOperator("Müller");
        var entry = EtbEntry.Create(At, EtbDirection.Internal, "Original", op);

        var result = entry.WithEdit("  Original  ", entry.From, entry.To, op, At.AddMinutes(1));

        Assert.Same(entry, result);
        Assert.Empty(result.Edits);
    }

    [Fact]
    public void Sequential_edits_accumulate_history_in_order()
    {
        var op = new SessionOperator("Müller");
        var entry = EtbEntry.Create(At, EtbDirection.Internal, "v1", op);

        var afterFirst = entry.WithEdit("v2", entry.From, entry.To, op, At.AddMinutes(1));
        var afterSecond = afterFirst.WithEdit("v3", afterFirst.From, afterFirst.To, op, At.AddMinutes(2));

        Assert.Equal("v3", afterSecond.Text);
        Assert.Equal(2, afterSecond.Edits.Count);
        Assert.Equal("v1", afterSecond.Edits[0].PreviousText);
        Assert.Equal("v2", afterSecond.Edits[1].PreviousText);
    }

    [Fact]
    public void WithEdit_changes_from_and_to_and_records_them_in_the_prior_version()
    {
        var op = new SessionOperator("Müller");
        var entry = EtbEntry.Create(At, EtbDirection.Incoming, "Lage", op, from: "Florian 1/44", to: "ELW 1");

        var edited = entry.WithEdit("Lage", "Florian 1/46", "  ", op, At.AddMinutes(1));

        Assert.Equal("Florian 1/46", edited.From);
        Assert.Null(edited.To);
        Assert.Equal("Lage", edited.Text);
        var edit = Assert.Single(edited.Edits);
        Assert.Equal("Lage", edit.PreviousText);
        Assert.Equal("Florian 1/44", edit.PreviousFrom);
        Assert.Equal("ELW 1", edit.PreviousTo);
    }

    [Fact]
    public void WithEdit_keeps_a_null_von_or_an_as_it_is_and_clears_a_blank_one()
    {
        var op = new SessionOperator("Müller");
        var entry = EtbEntry.Create(At, EtbDirection.Incoming, "Lage", op, from: "Florian 1/44", to: "ELW 1");

        var edited = entry.WithEdit("Lage korrigiert", null, string.Empty, op, At.AddMinutes(1));

        Assert.Equal("Florian 1/44", edited.From);
        Assert.Null(edited.To);
    }

    [Fact]
    public void WithEdit_records_the_unchanged_from_and_to_alongside_a_text_correction()
    {
        var op = new SessionOperator("Müller");
        var entry = EtbEntry.Create(At, EtbDirection.Incoming, "Lage", op, from: "ILS");

        var edited = entry.WithEdit("Lage korrigiert", "ILS", null, op, At.AddMinutes(1));

        var edit = Assert.Single(edited.Edits);
        Assert.Equal("ILS", edit.PreviousFrom);
        Assert.Null(edit.PreviousTo);
    }

    [Fact]
    public void WithEdit_is_a_no_op_when_from_and_to_differ_only_by_surrounding_whitespace()
    {
        var op = new SessionOperator("Müller");
        var entry = EtbEntry.Create(At, EtbDirection.Incoming, "Lage", op, from: "ILS");

        var result = entry.WithEdit("Lage", "  ILS ", string.Empty, op, At.AddMinutes(1));

        Assert.Same(entry, result);
    }

    [Fact]
    public void WithEdit_rejects_a_from_longer_than_the_limit()
    {
        var op = new SessionOperator("Müller");
        var entry = EtbEntry.Create(At, EtbDirection.Incoming, "Lage", op);
        var tooLong = new string('x', EtbEntry.MaxPartyLength + 1);

        var fromError = Assert.Throws<ArgumentException>(() => entry.WithEdit("Lage", tooLong, null, op, At.AddMinutes(1)));
        Assert.Throws<ArgumentException>(() => entry.WithEdit("Lage", null, tooLong, op, At.AddMinutes(1)));
        Assert.StartsWith($"Von/An ist länger als das Limit von {EtbEntry.MaxPartyLength} Zeichen.", fromError.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WithEdit_accepts_a_from_at_exactly_the_limit()
    {
        var op = new SessionOperator("Müller");
        var entry = EtbEntry.Create(At, EtbDirection.Incoming, "Lage", op);
        var atLimit = new string('x', EtbEntry.MaxPartyLength);

        var edited = entry.WithEdit("Lage", atLimit, null, op, At.AddMinutes(1));

        Assert.Equal(atLimit, edited.From);
    }

    [Fact]
    public void WithEdit_keeps_an_over_long_von_written_before_the_cap_through_a_text_correction()
    {
        var op = new SessionOperator("Müller");
        var longCallSign = new string('x', EtbEntry.MaxPartyLength + 1);
        var entry = EtbEntry.Create(At, EtbDirection.Incoming, "Lage", op, from: longCallSign);

        var edited = entry.WithEdit("Lage korrigiert", longCallSign, null, op, At.AddMinutes(1));

        Assert.Equal(longCallSign, edited.From);
    }

    [Fact]
    public void EditChangedParties_says_which_edits_changed_von_or_an()
    {
        var op = new SessionOperator("Müller");
        var entry = EtbEntry.Create(At, EtbDirection.Incoming, "v1", op, from: "Florian 1/44");

        var edited = entry
            .WithEdit("v2", "Florian 1/44", null, op, At.AddMinutes(1))
            .WithEdit("v2", "Florian 1/46", null, op, At.AddMinutes(2))
            .WithEdit("v3", "Florian 1/46", null, op, At.AddMinutes(3));

        Assert.False(edited.EditChangedParties(0));
        Assert.True(edited.EditChangedParties(1));
        Assert.False(edited.EditChangedParties(2));
    }
}
