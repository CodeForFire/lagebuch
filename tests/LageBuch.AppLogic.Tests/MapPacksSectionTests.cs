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
        library.Preinstall(Sta with { Version = 0 });
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
        var library = new FakeRegionPackLibrary { Catalogue = [Ffb with { Version = 2 }], InstallGate = new TaskCompletionSource() };
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
    public async Task While_the_list_loads_no_pack_can_be_installed()
    {
        var library = new FakeRegionPackLibrary { Catalogue = [Ffb] };
        var section = new MapPacksSection("Karten", library);
        await section.LoadCatalogCommand.ExecuteAsync(null);
        library.CatalogGate = new TaskCompletionSource();

        var loading = section.LoadCatalogCommand.ExecuteAsync(null);

        Assert.True(section.IsBusy);
        Assert.False(section.InstallCommand.CanExecute(section.Available[0]));
        library.CatalogGate.SetResult();
        await loading;
        Assert.False(section.IsBusy);
    }

    [Fact]
    public async Task Loading_the_list_again_replaces_it()
    {
        var library = new FakeRegionPackLibrary { Catalogue = [Ffb] };
        var section = new MapPacksSection("Karten", library);

        await section.LoadCatalogCommand.ExecuteAsync(null);
        await section.LoadCatalogCommand.ExecuteAsync(null);

        Assert.Single(section.Available);
    }

    [Fact]
    public async Task An_empty_list_says_so()
    {
        var section = new MapPacksSection("Karten", new FakeRegionPackLibrary());

        await section.LoadCatalogCommand.ExecuteAsync(null);

        Assert.Equal("Die Liste enthält keine Kartenpakete.", section.Status);
    }

    [Fact]
    public async Task Installing_or_removing_nothing_does_nothing()
    {
        var library = new FakeRegionPackLibrary();
        string? question = null;
        var section = new MapPacksSection("Karten", library, (q, _) => question = q);

        await section.InstallCommand.ExecuteAsync(null);
        section.RemoveCommand.Execute(null);

        Assert.Equal(0, library.InstallCount);
        Assert.Null(question);
    }

    [Fact]
    public async Task The_first_install_shows_the_list_of_installed_packs()
    {
        var library = new FakeRegionPackLibrary { Catalogue = [Ffb] };
        var section = new MapPacksSection("Karten", library);
        await section.LoadCatalogCommand.ExecuteAsync(null);
        var changed = new List<string?>();
        section.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        await section.InstallCommand.ExecuteAsync(section.Available[0]);

        Assert.Contains(nameof(MapPacksSection.HasInstalled), changed);
    }

    [Fact]
    public void Removing_a_pack_that_is_not_shown_keeps_the_one_that_is()
    {
        var library = new FakeRegionPackLibrary();
        library.Preinstall(Ffb);
        library.Preinstall(Sta);
        library.Preinstall(FakeRegionPackLibrary.Pack("ll", "Landkreis Landsberg am Lech"));
        var section = new MapPacksSection("Karten", library);
        section.SelectedPack = section.Installed[1];

        section.RemoveCommand.Execute(section.Installed[2]);

        Assert.Equal("Landkreis Starnberg", section.SelectedPack?.Name);
    }

    [Fact]
    public void Removing_the_shown_pack_lets_go_of_its_file_first()
    {
        var library = new FakeRegionPackLibrary();
        library.Preinstall(Ffb);
        var section = new MapPacksSection("Karten", library);
        object? shownWhileRemoving = "not called";
        library.Removing = () => shownWhileRemoving = section.Preview.TileSource;

        section.RemoveCommand.Execute(section.Installed[0]);

        Assert.Null(shownWhileRemoving);
    }

    [Fact]
    public async Task A_removed_pack_can_be_downloaded_again_from_the_list()
    {
        var library = new FakeRegionPackLibrary { Catalogue = [Ffb] };
        library.Preinstall(Ffb);
        var section = new MapPacksSection("Karten", library);
        await section.LoadCatalogCommand.ExecuteAsync(null);
        var raised = 0;
        section.InstallCommand.CanExecuteChanged += (_, _) => raised++;

        section.RemoveCommand.Execute(section.Installed[0]);

        Assert.Equal("HERUNTERLADEN", section.Available[0].ActionLabel);
        Assert.True(raised > 0);
    }

    [Fact]
    public void A_pack_shows_its_build_day_its_version_and_its_size_in_whole_megabytes_rounded_up()
    {
        var row = new InstalledMapPackRow(new InstalledRegionPack(Ffb with { SizeBytes = 264_978_779, Version = 3 }, "x"));

        Assert.Equal("Stand 27.10.2026 · Version 3 · 253 MB", row.Detail);
    }

    [Fact]
    public void A_build_time_that_is_no_timestamp_is_shown_as_it_came()
    {
        var row = new InstalledMapPackRow(new InstalledRegionPack(Ffb with { BuiltAt = "Herbst" }, "x"));

        Assert.Equal("Stand Herbst · Version 1 · 16 MB", row.Detail);
    }

    [Fact]
    public void A_rebuild_with_the_same_version_but_another_checksum_is_offered_as_an_update()
    {
        var row = new AvailableMapPackRow(Ffb with { Sha256 = new string('b', 64) }, Ffb);

        Assert.Equal("AKTUALISIEREN", row.ActionLabel);
    }
}
