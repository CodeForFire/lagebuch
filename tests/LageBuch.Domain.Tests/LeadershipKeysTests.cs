using LageBuch.Domain.Leadership;

namespace LageBuch.Domain.Tests;

public class LeadershipKeysTests
{
    [Fact]
    public void Keys_round_trip_every_value()
    {
        foreach (var kind in Enum.GetValues<CommandNodeKind>())
        {
            Assert.Equal(kind, LeadershipKeys.KindFrom(LeadershipKeys.KindKey(kind)));
        }

        foreach (var slot in Enum.GetValues<RoleSlot>())
        {
            Assert.Equal(slot, LeadershipKeys.SlotFrom(LeadershipKeys.SlotKey(slot)));
        }

        foreach (var stufe in Enum.GetValues<Fuehrungsstufe>())
        {
            Assert.Equal(stufe, LeadershipKeys.StufeFrom(LeadershipKeys.StufeKey(stufe)));
        }

        Assert.Equal("unterabschnitt", LeadershipKeys.KindKey(CommandNodeKind.Unterabschnitt));
        Assert.Equal("fuehrungsassistent", LeadershipKeys.SlotKey(RoleSlot.Fuehrungsassistent));
        Assert.Null(LeadershipKeys.SlotKey(RoleSlot.None));
        Assert.Equal("C", LeadershipKeys.StufeKey(Fuehrungsstufe.C));
        Assert.Equal(CommandNodeKind.Partner, LeadershipKeys.KindFrom(" PARTNER "));
        Assert.Equal(Fuehrungsstufe.B, LeadershipKeys.StufeFrom("b"));
    }

    [Fact]
    public void Unknown_kind_key_reads_as_abschnitt()
    {
        Assert.Equal(CommandNodeKind.Abschnitt, LeadershipKeys.KindFrom("zug"));
        Assert.Equal(CommandNodeKind.Abschnitt, LeadershipKeys.KindFrom(null));
    }

    [Fact]
    public void Unknown_slot_key_reads_as_none()
    {
        Assert.Equal(RoleSlot.None, LeadershipKeys.SlotFrom("chef"));
        Assert.Equal(RoleSlot.None, LeadershipKeys.SlotFrom(null));
    }

    [Fact]
    public void Unknown_stufe_key_reads_as_null()
    {
        Assert.Null(LeadershipKeys.StufeFrom("E"));
        Assert.Null(LeadershipKeys.StufeFrom(null));
    }
}
