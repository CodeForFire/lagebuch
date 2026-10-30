using LageBuch.Domain.Leadership;

namespace LageBuch.Domain.Tests;

public class CommandNodeTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 27, 14, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public void Create_trims_the_name_and_blanks_become_null()
    {
        var node = CommandNode.Create(CommandNodeKind.Abschnitt, "  Brandabschnitt Nord ", null);

        Assert.Equal("Brandabschnitt Nord", node.Name);
        Assert.True(node.IsLive);
        Assert.Null(node.Auftrag);

        var edited = node.WithDetails("  Nord 2 ", "  ", " Hof ", string.Empty, " FL Nord ", "  ");
        Assert.Equal("Nord 2", edited.Name);
        Assert.Null(edited.Auftrag);
        Assert.Equal("Hof", edited.Standort);
        Assert.Null(edited.Funkgruppe);
        Assert.Equal("FL Nord", edited.Rufname);
        Assert.Null(edited.Telefon);
    }

    [Fact]
    public void Create_refuses_a_blank_name()
    {
        var ex = Assert.Throws<ArgumentException>(() => CommandNode.Create(CommandNodeKind.Abschnitt, "   ", null));

        Assert.StartsWith("Name darf nicht leer sein.", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_refuses_a_name_longer_than_200()
    {
        Assert.Throws<ArgumentException>(() => CommandNode.Create(CommandNodeKind.Abschnitt, new string('x', 201), null));
        _ = CommandNode.Create(CommandNodeKind.Abschnitt, new string('x', 200), null);
    }

    [Fact]
    public void WithDetails_refuses_an_auftrag_longer_than_1000()
    {
        var node = CommandNode.Create(CommandNodeKind.Abschnitt, "A", null);

        Assert.Throws<ArgumentException>(() => node.WithDetails("A", new string('x', 1001), null, null, null, null));
        Assert.Equal(1000, node.WithDetails("A", new string('x', 1000), null, null, null, null).Auftrag?.Length);
    }

    [Fact]
    public void WithDetails_refuses_a_telefon_longer_than_50()
    {
        var node = CommandNode.Create(CommandNodeKind.Abschnitt, "A", null);

        Assert.Throws<ArgumentException>(() => node.WithDetails("A", null, null, null, null, new string('1', 51)));
    }

    [Fact]
    public void Unterabschnitt_needs_a_parent()
    {
        var ex = Assert.Throws<ArgumentException>(() => CommandNode.Create(CommandNodeKind.Unterabschnitt, "U", null));
        Assert.StartsWith("Ein Unterabschnitt braucht einen Abschnitt.", ex.Message, StringComparison.Ordinal);

        var parent = CommandNode.Create(CommandNodeKind.Abschnitt, "A", null);
        Assert.Equal(parent.Id, CommandNode.Create(CommandNodeKind.Unterabschnitt, "U", parent.Id).ParentId);
    }

    [Fact]
    public void Abschnitt_bereitstellung_and_partner_take_no_parent()
    {
        foreach (var kind in new[] { CommandNodeKind.Abschnitt, CommandNodeKind.Bereitstellung, CommandNodeKind.Partner })
        {
            var ex = Assert.Throws<ArgumentException>(() => CommandNode.Create(kind, "X", Guid.NewGuid()));
            Assert.StartsWith("Nur ein Unterabschnitt liegt in einem Abschnitt.", ex.Message, StringComparison.Ordinal);
            Assert.Null(CommandNode.Create(kind, "X", null).ParentId);
        }
    }

    [Fact]
    public void DissolvedAtTime_stamps_once_and_refuses_a_second_time()
    {
        var node = CommandNode.Create(CommandNodeKind.Abschnitt, "A", null);

        var dissolved = node.DissolvedAtTime(T0);

        Assert.False(dissolved.IsLive);
        Assert.Equal(T0, dissolved.DissolvedAt);
        var ex = Assert.Throws<InvalidOperationException>(() => dissolved.DissolvedAtTime(T0));
        Assert.Equal("Abschnitt ist bereits aufgelöst.", ex.Message);
    }

    [Fact]
    public void Rehydrate_keeps_every_field()
    {
        var id = Guid.NewGuid();
        var node = CommandNode.Rehydrate(id, CommandNodeKind.Partner, "Polizei", null, "a", "b", "c", "d", "e", T0, new Funkgruppen(["f"], ["g"]));

        Assert.Equal(id, node.Id);
        Assert.Equal("Polizei", node.Name);
        Assert.Equal(("a", "b", "c", "d", "e"), (node.Auftrag, node.Standort, node.Funkgruppe, node.Rufname, node.Telefon));
        Assert.Equal(T0, node.DissolvedAt);
        Assert.Equal(new Funkgruppen(["f"], ["g"]), node.Funkgruppen);
        Assert.Equal(Funkgruppen.Empty, CommandNode.Rehydrate(id, CommandNodeKind.Abschnitt, "Nord", null, null, null, null, null, null, null).Funkgruppen);
    }

    [Fact]
    public void LeadershipPost_normalizes_and_enforces_caps()
    {
        var post = new LeadershipPost(" Hof ", "  ", " 0171 ", new Funkgruppen([" FW 1000 "], null)).Normalized();

        Assert.Equal(new LeadershipPost("Hof", null, "0171", new Funkgruppen(["FW 1000"], null)), post);
        Assert.Equal(new LeadershipPost(null, null, null, Funkgruppen.Empty), LeadershipPost.Empty);
        Assert.Throws<ArgumentException>(() => new LeadershipPost(new string('x', 201), null, null, Funkgruppen.Empty).Normalized());
        Assert.Throws<ArgumentException>(() => new LeadershipPost(null, null, new string('1', 51), Funkgruppen.Empty).Normalized());
        Assert.Throws<ArgumentException>(
            () => new LeadershipPost(null, null, null, new Funkgruppen(null, ["1", "2", "3", "4"])).Normalized());
    }

    [Fact]
    public void A_partner_takes_funkgruppen_and_no_single_funkgruppe()
    {
        var partner = CommandNode.Create(CommandNodeKind.Partner, "Polizei", null);

        var edited = partner.WithDetails("Polizei", null, null, null, null, null, new Funkgruppen([" POL 1 "], ["301_F"]));

        Assert.Equal(new Funkgruppen(["POL 1"], ["301_F"]), edited.Funkgruppen);
        Assert.Null(edited.Funkgruppe);
        var ex = Assert.Throws<ArgumentException>(() => partner.WithDetails("Polizei", null, null, "TMO POL 1", null, null));
        Assert.StartsWith("Eine Einsatzleitung hat Funkgruppen TMO und DMO.", ex.Message, StringComparison.Ordinal);
        var tooLong = Assert.Throws<ArgumentException>(
            () => CommandNode.Create(CommandNodeKind.Abschnitt, "Nord", null).WithDetails("Nord", null, null, new string('x', 201), null, null));
        Assert.StartsWith("Funkgruppe ist länger", tooLong.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_a_partner_takes_funkgruppen()
    {
        var abschnitt = CommandNode.Create(CommandNodeKind.Abschnitt, "Nord", null);

        var ex = Assert.Throws<ArgumentException>(
            () => abschnitt.WithDetails("Nord", null, null, null, null, null, new Funkgruppen(["A"], null)));

        Assert.StartsWith("Nur eine Einsatzleitung hat Funkgruppen TMO und DMO.", ex.Message, StringComparison.Ordinal);
        Assert.Equal(Funkgruppen.Empty, abschnitt.WithDetails("Nord", null, null, "DMO 301_F", null, null, Funkgruppen.Empty).Funkgruppen);
    }
}
