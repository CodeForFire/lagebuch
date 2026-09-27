using LageBuch.Domain.Involved;

namespace LageBuch.Domain.Tests;

public class InvolvedPartyTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 27, 14, 0, 0, TimeSpan.FromHours(2));

    private static readonly SessionOperator Op = new("Muster", "FFB 12/1");

    [Fact]
    public void Create_trims_the_fields_and_stamps_the_operator()
    {
        var party = InvolvedParty.Create(T0, "  Erika Beispiel ", " 0171 0000001 ", "  Hauseigentümerin ", Op);

        Assert.Equal("Erika Beispiel", party.Name);
        Assert.Equal("0171 0000001", party.Phone);
        Assert.Equal("Hauseigentümerin", party.Notes);
        Assert.Equal(Op.Display, party.CreatedBy);
        Assert.Equal(T0, party.CreatedAt);
        Assert.NotEqual(Guid.Empty, party.Id);
    }

    [Fact]
    public void Create_turns_blank_phone_and_notes_into_null()
    {
        var party = InvolvedParty.Create(T0, "Erika Beispiel", "   ", string.Empty, Op);

        Assert.Null(party.Phone);
        Assert.Null(party.Notes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_a_blank_name(string name)
    {
        var ex = Assert.Throws<ArgumentException>(() => InvolvedParty.Create(T0, name, null, null, Op));
        Assert.StartsWith("Name darf nicht leer sein.", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_rejects_a_name_over_the_cap()
    {
        var name = new string('a', InvolvedParty.MaxNameLength + 1);
        Assert.Throws<ArgumentException>(() => InvolvedParty.Create(T0, name, null, null, Op));
    }

    [Fact]
    public void Create_accepts_a_name_exactly_at_the_cap()
    {
        var name = new string('a', InvolvedParty.MaxNameLength);
        Assert.Equal(name, InvolvedParty.Create(T0, name, null, null, Op).Name);
    }

    [Fact]
    public void Create_rejects_a_phone_over_the_cap()
    {
        var phone = new string('1', InvolvedParty.MaxPhoneLength + 1);
        Assert.Throws<ArgumentException>(() => InvolvedParty.Create(T0, "Erika Beispiel", phone, null, Op));
    }

    [Fact]
    public void Create_rejects_notes_over_the_cap()
    {
        var notes = new string('x', InvolvedParty.MaxNotesLength + 1);
        Assert.Throws<ArgumentException>(() => InvolvedParty.Create(T0, "Erika Beispiel", null, notes, Op));
    }

    [Fact]
    public void WithDetails_keeps_id_and_creation_stamp_and_normalises_like_create()
    {
        var party = InvolvedParty.Create(T0, "Erika Beispiel", null, null, Op);

        var updated = party.WithDetails(" Max Beispiel ", " 110 ", "  ");

        Assert.Equal(party.Id, updated.Id);
        Assert.Equal(party.CreatedAt, updated.CreatedAt);
        Assert.Equal(party.CreatedBy, updated.CreatedBy);
        Assert.Equal("Max Beispiel", updated.Name);
        Assert.Equal("110", updated.Phone);
        Assert.Null(updated.Notes);
    }

    [Fact]
    public void WithDetails_rejects_a_blank_name()
    {
        var party = InvolvedParty.Create(T0, "Erika Beispiel", null, null, Op);
        Assert.Throws<ArgumentException>(() => party.WithDetails(" ", null, null));
    }

    [Fact]
    public void Rehydrate_restores_every_field()
    {
        var id = Guid.NewGuid();
        var party = InvolvedParty.Rehydrate(id, "Erika Beispiel", "0171 0000001", "Halterin", "Muster", T0);

        Assert.Equal(id, party.Id);
        Assert.Equal("Erika Beispiel", party.Name);
        Assert.Equal("0171 0000001", party.Phone);
        Assert.Equal("Halterin", party.Notes);
        Assert.Equal("Muster", party.CreatedBy);
        Assert.Equal(T0, party.CreatedAt);
    }
}
