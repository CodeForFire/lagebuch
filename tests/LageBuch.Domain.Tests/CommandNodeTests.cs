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

        var edited = node.WithDetails("  Nord 2 ", "  ", " Hof ", null, " FL Nord ", "  ");
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

    [Theory]
    [InlineData(CommandNodeKind.Partner)]
    [InlineData(CommandNodeKind.Abschnitt)]
    [InlineData(CommandNodeKind.Bereitstellung)]
    public void Every_kind_takes_funkgruppen_and_no_single_funkgruppe(CommandNodeKind kind)
    {
        var node = CommandNode.Create(kind, "Nord", null);

        var edited = node.WithDetails("Nord", null, null, null, null, null, new Funkgruppen([" FW 1 "], ["301_F"]));

        Assert.Equal(new Funkgruppen(["FW 1"], ["301_F"]), edited.Funkgruppen);
        Assert.Null(edited.Funkgruppe);
        var ex = Assert.Throws<ArgumentException>(() => node.WithDetails("Nord", null, null, "TMO FW 1", null, null));
        Assert.StartsWith("Funkgruppen werden als TMO und DMO erfasst.", ex.Message, StringComparison.Ordinal);
        Assert.Null(node.WithDetails("Nord", null, null, "  ", null, null).Funkgruppe);
    }

    [Fact]
    public void An_edit_clears_a_single_funkgruppe_an_old_file_carried()
    {
        var old = CommandNode.Rehydrate(Guid.NewGuid(), CommandNodeKind.Abschnitt, "Nord", null, null, null, "TG 2", null, null, null);

        var edited = old.WithDetails("Nord", null, null, null, null, null, new Funkgruppen(["TG 2"], null));

        Assert.Null(edited.Funkgruppe);
        Assert.Equal(new Funkgruppen(["TG 2"], null), edited.Funkgruppen);
    }

    [Fact]
    public void Every_kind_takes_a_fuehrungsfahrzeug_trimmed_and_capped()
    {
        foreach (var kind in Enum.GetValues<CommandNodeKind>())
        {
            var node = CommandNode.Create(kind, "Nord", kind == CommandNodeKind.Unterabschnitt ? Guid.NewGuid() : null);

            var edited = node.WithDetails("Nord", null, null, null, null, null, null, " FFB 1/12/1 ");

            Assert.Equal("FFB 1/12/1", edited.Fuehrungsfahrzeug);
            Assert.Null(node.WithDetails("Nord", null, null, null, null, null, null, "   ").Fuehrungsfahrzeug);
            var ex = Assert.Throws<ArgumentException>(
                () => node.WithDetails("Nord", null, null, null, null, null, null, new string('x', 201)));
            Assert.StartsWith("Führungsfahrzeug ist länger", ex.Message, StringComparison.Ordinal);
            Assert.Equal(200, node.WithDetails("Nord", null, null, null, null, null, null, new string('x', 200)).Fuehrungsfahrzeug?.Length);
        }
    }

    [Fact]
    public void Rehydrate_keeps_the_fuehrungsfahrzeug()
    {
        var node = CommandNode.Rehydrate(
            Guid.NewGuid(), CommandNodeKind.Abschnitt, "Nord", null, null, null, null, null, null, null, null, "FFB 40/1");

        Assert.Equal("FFB 40/1", node.Fuehrungsfahrzeug);
        Assert.Null(CommandNode.Rehydrate(Guid.NewGuid(), CommandNodeKind.Abschnitt, "Nord", null, null, null, null, null, null, null).Fuehrungsfahrzeug);
    }

    [Fact]
    public void LeadershipPost_normalizes_and_caps_the_fuehrungsfahrzeug()
    {
        Assert.Equal("FFB 1/12/1", new LeadershipPost(null, null, null, Funkgruppen.Empty, " FFB 1/12/1 ").Normalized().Fuehrungsfahrzeug);
        Assert.Null(new LeadershipPost(null, null, null, Funkgruppen.Empty, "  ").Normalized().Fuehrungsfahrzeug);
        Assert.Null(LeadershipPost.Empty.Fuehrungsfahrzeug);
        var ex = Assert.Throws<ArgumentException>(() => new LeadershipPost(null, null, null, Funkgruppen.Empty, new string('x', 201)).Normalized());
        Assert.StartsWith("Führungsfahrzeug ist länger", ex.Message, StringComparison.Ordinal);
    }
}
