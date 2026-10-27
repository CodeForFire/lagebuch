using LageBuch.AppLogic.Services.Map;
using LageBuch.AppLogic.ViewModels;

namespace LageBuch.AppLogic.Tests;

public class MapPacksSectionTests
{
    private static readonly RegionPackInfo Ffb = FakeRegionPackLibrary.Pack("ffb", "Landkreis Fürstenfeldbruck");
    private static readonly RegionPackInfo Sta = FakeRegionPackLibrary.Pack("sta", "Landkreis Starnberg");

    [Fact]
    public void The_installed_packs_are_listed_and_the_first_one_is_shown()
    {
        var library = new FakeRegionPackLibrary();
        library.Preinstall(Sta);
        library.Preinstall(Ffb);

        var section = new MapPacksSection("Karten", library);

        Assert.Equal(["Landkreis Starnberg", "Landkreis Fürstenfeldbruck"], section.Installed.Select(r => r.Name));
        Assert.Same(section.Installed[0], section.SelectedPack);
        Assert.Equal(Sta.Attribution, section.Preview.Attribution);
        Assert.True(section.HasInstalled);
    }

    [Fact]
    public void Without_a_pack_nothing_is_shown()
    {
        var section = new MapPacksSection("Karten", new FakeRegionPackLibrary());

        Assert.False(section.HasInstalled);
        Assert.Null(section.Preview.TileSource);
    }

    [Fact]
    public async Task The_catalogue_tells_a_missing_pack_from_an_installed_and_an_outdated_one()
    {
        var library = new FakeRegionPackLibrary();
        library.Preinstall(Ffb);
        library.Preinstall(Sta with { BuiltAt = "2026-01-01" });
        library.Catalogue = [Ffb, Sta, FakeRegionPackLibrary.Pack("ll", "Landkreis Landsberg am Lech")];
        var section = new MapPacksSection("Karten", library);

        await section.LoadCatalogCommand.ExecuteAsync(null);

        Assert.Equal(
            ["INSTALLIERT", "HERUNTERLADEN", "AKTUALISIEREN"],
            section.Available.Select(r => r.ActionLabel));
        Assert.False(section.InstallCommand.CanExecute(section.Available[0]));
        Assert.True(section.InstallCommand.CanExecute(section.Available[2]));
    }

    [Fact]
    public async Task A_catalogue_that_cannot_be_fetched_says_why()
    {
        var library = new FakeRegionPackLibrary { Failure = new RegionPackException("Die Liste der Kartenpakete ist nicht erreichbar. Internetverbindung prüfen.") };
        var section = new MapPacksSection("Karten", library);

        await section.LoadCatalogCommand.ExecuteAsync(null);

        Assert.Equal("Die Liste der Kartenpakete ist nicht erreichbar. Internetverbindung prüfen.", section.Status);
        Assert.False(section.IsBusy);
    }

    [Fact]
    public async Task An_install_lists_the_pack_shows_it_and_marks_it_installed()
    {
        var library = new FakeRegionPackLibrary { Catalogue = [Ffb] };
        var section = new MapPacksSection("Karten", library);
        await section.LoadCatalogCommand.ExecuteAsync(null);

        await section.InstallCommand.ExecuteAsync(section.Available[0]);

        Assert.Equal("Landkreis Fürstenfeldbruck", Assert.Single(section.Installed).Name);
        Assert.Same(section.Installed[0], section.SelectedPack);
        Assert.Equal("„Landkreis Fürstenfeldbruck“ ist installiert.", section.Status);
        Assert.Equal("INSTALLIERT", section.Available[0].ActionLabel);
    }

    [Fact]
    public async Task While_a_pack_downloads_no_second_one_can_start()
    {
        var library = new FakeRegionPackLibrary { Catalogue = [Ffb, Sta], InstallGate = new TaskCompletionSource() };
        var section = new MapPacksSection("Karten", library);
        await section.LoadCatalogCommand.ExecuteAsync(null);

        var running = section.InstallCommand.ExecuteAsync(section.Available[0]);

        Assert.True(section.IsBusy);
        Assert.False(section.InstallCommand.CanExecute(section.Available[1]));
        Assert.False(section.LoadCatalogCommand.CanExecute(null));
        library.InstallGate.SetResult();
        await running;
        Assert.False(section.IsBusy);
        Assert.Equal(1, library.InstallCount);
    }

    [Fact]
    public async Task Reinstalling_the_shown_pack_lets_go_of_its_file_first()
    {
        var library = new FakeRegionPackLibrary { Catalogue = [Ffb with { BuiltAt = "2026-10-01" }], InstallGate = new TaskCompletionSource() };
        library.Preinstall(Ffb);
        var section = new MapPacksSection("Karten", library);
        await section.LoadCatalogCommand.ExecuteAsync(null);

        var running = section.InstallCommand.ExecuteAsync(section.Available[0]);

        Assert.Null(section.Preview.TileSource);
        library.InstallGate.SetResult();
        await running;
        Assert.NotNull(section.Preview.TileSource);
    }

    [Fact]
    public async Task A_failed_install_says_why_and_keeps_what_was_there()
    {
        var library = new FakeRegionPackLibrary { Catalogue = [Sta] };
        library.Preinstall(Ffb);
        var section = new MapPacksSection("Karten", library);
        await section.LoadCatalogCommand.ExecuteAsync(null);
        library.Failure = new RegionPackException("Das Kartenpaket ist beschädigt.");

        await section.InstallCommand.ExecuteAsync(section.Available[0]);

        Assert.Equal("Das Kartenpaket ist beschädigt.", section.Status);
        Assert.Equal("Landkreis Fürstenfeldbruck", Assert.Single(section.Installed).Name);
        Assert.False(section.IsBusy);
    }

    [Fact]
    public void Removing_a_pack_asks_first_and_deletes_it_on_confirm()
    {
        var library = new FakeRegionPackLibrary();
        library.Preinstall(Ffb);
        string? question = null;
        Action? confirm = null;
        var section = new MapPacksSection("Karten", library, (q, onConfirm) => (question, confirm) = (q, onConfirm));

        section.RemoveCommand.Execute(section.Installed[0]);

        Assert.Equal("„Landkreis Fürstenfeldbruck“ aus Karten entfernen?", question);
        Assert.Single(library.GetInstalled());
        Assert.NotNull(confirm);
        confirm();
        Assert.Empty(library.GetInstalled());
        Assert.Empty(section.Installed);
        Assert.Null(section.SelectedPack);
        Assert.Null(section.Preview.TileSource);
    }

    [Fact]
    public void A_pack_that_cannot_be_deleted_says_why_and_stays_listed()
    {
        var library = new FakeRegionPackLibrary();
        library.Preinstall(Ffb);
        var section = new MapPacksSection("Karten", library);
        library.Failure = new RegionPackException("„Landkreis Fürstenfeldbruck“ konnte nicht gelöscht werden. Wird die Karte gerade angezeigt?");

        section.RemoveCommand.Execute(section.Installed[0]);

        Assert.StartsWith("„Landkreis Fürstenfeldbruck“ konnte nicht gelöscht werden.", section.Status, StringComparison.Ordinal);
        Assert.Single(section.Installed);
    }

    [Fact]
    public void The_size_is_shown_in_whole_megabytes_rounded_up()
    {
        var row = new InstalledMapPackRow(new InstalledRegionPack(Ffb with { SizeBytes = 15_782_458 }, "x"));

        Assert.Equal("Stand 2026-08-31 · 16 MB", row.Detail);
    }
}
