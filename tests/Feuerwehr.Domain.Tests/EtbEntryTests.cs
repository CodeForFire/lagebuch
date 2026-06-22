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
}
