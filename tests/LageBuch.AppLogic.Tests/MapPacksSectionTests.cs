using LageBuch.AppLogic.Services.Map;
using LageBuch.AppLogic.ViewModels;

namespace LageBuch.AppLogic.Tests;

public class MapPacksSectionTests
{
    private static readonly RegionPackInfo Ffb = FakeRegionPackLibrary.Pack("ffb", "Landkreis Fürstenfeldbruck");
    private static readonly RegionPackInfo Sta = FakeRegionPackLibrary.Pack("sta", "Landkreis Starnberg");
    private static readonly RegionPackInfo Ll = FakeRegionPackLibrary.Pack("ll", "Landkreis Landsberg am Lech");

    private static MapPackRow Row(MapPacksSection section, string slug) => section.Packs.Single(p => p.Slug == slug);

    [Fact]
    public void The_installed_packs_are_listed_by_name_and_the_first_one_is_shown()
    {
        var library = new FakeRegionPackLibrary();
        library.Preinstall(Sta);
        library.Preinstall(Ffb);

        var section = new MapPacksSection("Karten", library);

        Assert.Equal(["Landkreis Fürstenfeldbruck", "Landkreis Starnberg"], section.Packs.Select(r => r.Name));
        Assert.Same(section.Packs[0], section.SelectedPack);
        Assert.Equal(Ffb.Attribution, section.Preview.Attribution);
        Assert.True(section.HasInstalled);
        Assert.All(section.Packs, p => Assert.Equal(MapPackState.Installed, p.State));
    }

    [Fact]
    public void Without_a_pack_nothing_is_shown()
    {
        var section = new MapPacksSection("Karten", new FakeRegionPackLibrary());

        Assert.False(section.HasInstalled);
        Assert.Empty(section.Packs);
        Assert.Null(section.Preview.TileSource);
        Assert.Equal("Nichts installiert", section.RailSummary);
    }

    [Fact]
    public async Task The_catalogue_joins_the_installed_packs_in_one_list_each_with_its_state()
    {
        var library = new FakeRegionPackLibrary();
        library.Preinstall(Ffb);
        library.Preinstall(Sta with { Version = 0 });
        library.Catalogue = [Sta, Ffb, Ll];
        var section = new MapPacksSection("Karten", library);

        await section.LoadCatalogCommand.ExecuteAsync(null);

        Assert.Equal(["ffb", "ll", "sta"], section.Packs.Select(p => p.Slug));
        Assert.Equal(
            [MapPackState.Installed, MapPackState.NotInstalled, MapPackState.UpdateAvailable],
            section.Packs.Select(p => p.State));
        Assert.Equal([null, "HERUNTERLADEN", "AKTUALISIEREN"], section.Packs.Select(r => r.ActionLabel));
        Assert.False(section.InstallCommand.CanExecute(Row(section, "ffb")));
        Assert.True(section.InstallCommand.CanExecute(Row(section, "ll")));
        Assert.Equal([true, false, true], section.Packs.Select(p => p.ShowsRemove));
        Assert.Equal("2 installiert · 1 Update", section.RailSummary);
    }

    [Fact]
    public async Task An_installed_pack_the_catalogue_no_longer_offers_stays_listed_as_installed()
    {
        var library = new FakeRegionPackLibrary { Catalogue = [Sta] };
        library.Preinstall(Ffb);
        var section = new MapPacksSection("Karten", library);

        await section.LoadCatalogCommand.ExecuteAsync(null);

        Assert.Equal(MapPackState.Installed, Row(section, "ffb").State);
        Assert.Null(Row(section, "ffb").ActionLabel);
    }

    [Fact]
    public async Task A_catalogue_that_cannot_be_fetched_says_why_and_keeps_the_installed_packs()
    {
        var library = new FakeRegionPackLibrary { Failure = new RegionPackException("Die Liste der Kartenpakete ist nicht erreichbar. Internetverbindung prüfen.") };
        library.Preinstall(Ffb);
        var section = new MapPacksSection("Karten", library);

        await section.LoadCatalogCommand.ExecuteAsync(null);

        Assert.Equal("Die Liste der Kartenpakete ist nicht erreichbar. Internetverbindung prüfen. Installierte Karten funktionieren weiter.", section.Status);
        Assert.Equal("ffb", Assert.Single(section.Packs).Slug);
        Assert.False(section.IsBusy);
        Assert.False(section.IsCatalogLoading);
    }

    [Fact]
    public async Task Without_an_installed_pack_a_catalogue_failure_says_only_why()
    {
        var library = new FakeRegionPackLibrary { Failure = new RegionPackException("Die Liste der Kartenpakete ist nicht erreichbar. Internetverbindung prüfen.") };
        var section = new MapPacksSection("Karten", library);

        await section.LoadCatalogCommand.ExecuteAsync(null);

        Assert.Equal("Die Liste der Kartenpakete ist nicht erreichbar. Internetverbindung prüfen.", section.Status);
    }

    [Fact]
    public async Task The_catalogue_is_fetched_once_on_the_first_look_and_again_only_after_a_failure()
    {
        var library = new FakeRegionPackLibrary { Catalogue = [Ffb], Failure = new RegionPackException("weg") };
        var section = new MapPacksSection("Karten", library);

        section.EnsureCatalogLoaded();
        Assert.Empty(section.Packs);

        library.Failure = null;
        library.CatalogGate = new TaskCompletionSource();
        section.EnsureCatalogLoaded();
        Assert.True(section.IsCatalogLoading);
        section.EnsureCatalogLoaded();
        var loading = section.LoadCatalogCommand.ExecutionTask;
        library.CatalogGate.SetResult();
        await Assert.IsAssignableFrom<Task>(loading);

        Assert.Single(section.Packs);
        library.Catalogue = [Ffb, Sta];
        section.EnsureCatalogLoaded();
        Assert.Single(section.Packs);
    }

    [Fact]
    public async Task An_install_shows_the_pack_and_marks_it_installed()
    {
        var library = new FakeRegionPackLibrary { Catalogue = [Ffb] };
        var section = new MapPacksSection("Karten", library);
        await section.LoadCatalogCommand.ExecuteAsync(null);

        await section.InstallCommand.ExecuteAsync(Row(section, "ffb"));

        var row = Assert.Single(section.Packs);
        Assert.Equal(MapPackState.Installed, row.State);
        Assert.Same(row, section.SelectedPack);
        Assert.Equal("„Landkreis Fürstenfeldbruck“ ist installiert.", section.Status);
        Assert.Equal("1 installiert", section.RailSummary);
    }

    [Fact]
    public async Task The_progress_is_shown_in_the_row_that_downloads_and_in_the_rail()
    {
        var library = new FakeRegionPackLibrary { Catalogue = [Ffb with { SizeBytes = 109L * 1024 * 1024 }, Sta], InstallGate = new TaskCompletionSource() };
        var section = new MapPacksSection("Karten", library);
        await section.LoadCatalogCommand.ExecuteAsync(null);
        var row = Row(section, "ffb");

        var running = section.InstallCommand.ExecuteAsync(row);

        Assert.True(row.IsDownloading);
        Assert.False(row.ShowsAction);
        Assert.False(row.ShowsDetail);
        Assert.Equal("50 % von 109 MB", row.ProgressText);
        Assert.Equal("Lädt … 50 %", section.RailSummary);
        Assert.False(Row(section, "sta").IsDownloading);
        library.InstallGate.SetResult();
        await running;
        Assert.DoesNotContain(section.Packs, p => p.IsDownloading);
    }

    [Fact]
    public async Task A_cancelled_download_says_so_and_leaves_the_pack_as_it_was()
    {
        var library = new FakeRegionPackLibrary { Catalogue = [Ffb with { Version = 2 }], InstallGate = new TaskCompletionSource() };
        library.Preinstall(Ffb);
        var section = new MapPacksSection("Karten", library);
        await section.LoadCatalogCommand.ExecuteAsync(null);

        var running = section.InstallCommand.ExecuteAsync(Row(section, "ffb"));
        section.CancelInstallCommand.Execute(null);
        await running;

        Assert.Equal("Download abgebrochen.", section.Status);
        var row = Assert.Single(section.Packs);
        Assert.Equal(MapPackState.UpdateAvailable, row.State);
        Assert.Equal(1, row.Installed?.Info.Version);
        Assert.False(row.IsDownloading);
        Assert.False(section.IsBusy);
        Assert.Same(row, section.SelectedPack);
        Assert.NotNull(section.Preview.TileSource);
    }

    [Fact]
    public void Cancelling_with_no_download_running_does_nothing()
    {
        var section = new MapPacksSection("Karten", new FakeRegionPackLibrary());

        section.CancelInstallCommand.Execute(null);

        Assert.Null(section.Status);
    }

    [Fact]
    public async Task While_a_pack_downloads_no_second_one_can_start_nor_one_be_removed()
    {
        var library = new FakeRegionPackLibrary { Catalogue = [Ffb, Sta], InstallGate = new TaskCompletionSource() };
        library.Preinstall(Ll);
        var section = new MapPacksSection("Karten", library);
        await section.LoadCatalogCommand.ExecuteAsync(null);

        var running = section.InstallCommand.ExecuteAsync(Row(section, "ffb"));

        Assert.True(section.IsBusy);
        Assert.False(section.InstallCommand.CanExecute(Row(section, "sta")));
        Assert.False(section.RemoveCommand.CanExecute(Row(section, "ll")));
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

        var running = section.InstallCommand.ExecuteAsync(Row(section, "ffb"));

        Assert.Null(section.Preview.TileSource);
        library.InstallGate.SetResult();
        await running;
        Assert.NotNull(section.Preview.TileSource);
        Assert.Equal(MapPackState.Installed, Assert.Single(section.Packs).State);
    }

    [Fact]
    public async Task Installing_another_pack_keeps_the_shown_one()
    {
        var library = new FakeRegionPackLibrary { Catalogue = [Ffb, Sta] };
        library.Preinstall(Sta);
        var section = new MapPacksSection("Karten", library);
        await section.LoadCatalogCommand.ExecuteAsync(null);
        library.Failure = new RegionPackException("Das Kartenpaket ist beschädigt.");

        await section.InstallCommand.ExecuteAsync(Row(section, "ffb"));

        Assert.Equal("sta", section.SelectedPack?.Slug);
    }

    [Fact]
    public async Task Loading_the_list_keeps_the_open_map_where_it_is()
    {
        var library = new FakeRegionPackLibrary { Catalogue = [Ffb, Sta] };
        library.Preinstall(Ffb);
        var section = new MapPacksSection("Karten", library);
        var source = section.Preview.TileSource;

        await section.LoadCatalogCommand.ExecuteAsync(null);

        Assert.Same(source, section.Preview.TileSource);
    }

    [Fact]
    public async Task A_failed_install_says_why_and_keeps_what_was_there()
    {
        var library = new FakeRegionPackLibrary { Catalogue = [Sta] };
        library.Preinstall(Ffb);
        var section = new MapPacksSection("Karten", library);
        await section.LoadCatalogCommand.ExecuteAsync(null);
        library.Failure = new RegionPackException("Das Kartenpaket ist beschädigt.");

        await section.InstallCommand.ExecuteAsync(Row(section, "sta"));

        Assert.Equal("Das Kartenpaket ist beschädigt.", section.Status);
        Assert.Equal(MapPackState.Installed, Row(section, "ffb").State);
        Assert.Equal(MapPackState.NotInstalled, Row(section, "sta").State);
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

        section.RemoveCommand.Execute(section.Packs[0]);

        Assert.Equal("„Landkreis Fürstenfeldbruck“ aus Karten entfernen?", question);
        Assert.Single(library.GetInstalled());
        Assert.NotNull(confirm);
        confirm();
        Assert.Empty(library.GetInstalled());
        Assert.Empty(section.Packs);
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

        section.RemoveCommand.Execute(section.Packs[0]);

        Assert.StartsWith("„Landkreis Fürstenfeldbruck“ konnte nicht gelöscht werden.", section.Status, StringComparison.Ordinal);
        Assert.Single(section.Packs);
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
        Assert.True(section.IsCatalogLoading);
        Assert.False(section.InstallCommand.CanExecute(section.Packs[0]));
        library.CatalogGate.SetResult();
        await loading;
        Assert.False(section.IsBusy);
        Assert.False(section.IsCatalogLoading);
    }

    [Fact]
    public async Task Loading_the_list_again_replaces_it()
    {
        var library = new FakeRegionPackLibrary { Catalogue = [Ffb] };
        var section = new MapPacksSection("Karten", library);

        await section.LoadCatalogCommand.ExecuteAsync(null);
        await section.LoadCatalogCommand.ExecuteAsync(null);

        Assert.Single(section.Packs);
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
        await section.InstallCommand.ExecuteAsync(new MapPackRow(null, new InstalledRegionPack(Ffb, "x")));
        section.RemoveCommand.Execute(new MapPackRow(Ffb, null));

        Assert.Equal(0, library.InstallCount);
        Assert.Null(question);
    }

    [Fact]
    public async Task The_first_install_shows_the_map()
    {
        var library = new FakeRegionPackLibrary { Catalogue = [Ffb] };
        var section = new MapPacksSection("Karten", library);
        await section.LoadCatalogCommand.ExecuteAsync(null);
        var changed = new List<string?>();
        section.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        await section.InstallCommand.ExecuteAsync(Row(section, "ffb"));

        Assert.Contains(nameof(MapPacksSection.HasInstalled), changed);
        Assert.Contains(nameof(MapPacksSection.RailSummary), changed);
    }

    [Fact]
    public void Removing_a_pack_that_is_not_shown_keeps_the_one_that_is()
    {
        var library = new FakeRegionPackLibrary();
        library.Preinstall(Ffb);
        library.Preinstall(Sta);
        library.Preinstall(Ll);
        var section = new MapPacksSection("Karten", library);
        section.SelectedPack = Row(section, "sta");

        section.RemoveCommand.Execute(Row(section, "ll"));

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

        section.RemoveCommand.Execute(section.Packs[0]);

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

        section.RemoveCommand.Execute(section.Packs[0]);

        Assert.Equal("HERUNTERLADEN", Assert.Single(section.Packs).ActionLabel);
        Assert.True(raised > 0);
    }

    [Fact]
    public async Task Picking_a_pack_that_is_not_installed_shows_no_map_and_says_so()
    {
        var library = new FakeRegionPackLibrary { Catalogue = [Ffb, Sta] };
        library.Preinstall(Ffb);
        var section = new MapPacksSection("Karten", library);
        await section.LoadCatalogCommand.ExecuteAsync(null);

        section.SelectedPack = Row(section, "sta");

        Assert.True(section.HasNoPreview);
        Assert.Null(section.Preview.TileSource);
        section.SelectedPack = Row(section, "ffb");
        Assert.False(section.HasNoPreview);
        Assert.NotNull(section.Preview.TileSource);
    }

    [Fact]
    public async Task The_rail_counts_more_than_one_update()
    {
        var library = new FakeRegionPackLibrary { Catalogue = [Ffb with { Version = 2 }, Sta with { Version = 2 }] };
        library.Preinstall(Ffb);
        library.Preinstall(Sta);
        var section = new MapPacksSection("Karten", library);

        await section.LoadCatalogCommand.ExecuteAsync(null);

        Assert.Equal("2 installiert · 2 Updates", section.RailSummary);
    }

    [Fact]
    public void A_pack_shows_its_build_day_its_version_and_its_size_in_whole_megabytes_rounded_up()
    {
        var row = new MapPackRow(null, new InstalledRegionPack(Ffb with { SizeBytes = 264_978_779, Version = 3 }, "x"));

        Assert.Equal("Stand 27.10.2026 · Version 3 · 253 MB", row.Detail);
    }

    [Fact]
    public void A_build_time_that_is_no_timestamp_is_shown_as_it_came()
    {
        var row = new MapPackRow(Ffb with { BuiltAt = "Herbst" }, null);

        Assert.Equal("Stand Herbst · Version 1 · 16 MB", row.Detail);
    }

    [Fact]
    public void An_update_row_describes_the_installed_edition_and_names_the_new_version_or_build_day()
    {
        var installed = new InstalledRegionPack(Ffb, "x");

        var newer = new MapPackRow(Ffb with { Version = 2 }, installed);
        Assert.Equal("Neue Version 2 – installiert ist Version 1.", newer.UpdateReason);
        Assert.Equal("Stand 27.10.2026 · Version 1 · 16 MB", newer.Detail);
        Assert.Equal(
            "Neu gebaut am 28.01.2027 – installiert ist der Stand vom 27.10.2026.",
            new MapPackRow(Ffb with { Sha256 = new string('b', 64), BuiltAt = "2027-01-28T03:00:00Z" }, installed).UpdateReason);
    }

    [Fact]
    public void A_pack_not_yet_installed_or_already_current_gives_no_reason()
    {
        Assert.Null(new MapPackRow(Ffb, null).UpdateReason);
        Assert.Null(new MapPackRow(Ffb, new InstalledRegionPack(Ffb, "x")).UpdateReason);
        Assert.Null(new MapPackRow(null, new InstalledRegionPack(Ffb, "x")).UpdateReason);
    }

    [Fact]
    public void A_rebuild_with_the_same_version_but_another_checksum_is_offered_as_an_update()
    {
        var row = new MapPackRow(Ffb with { Sha256 = new string('b', 64) }, new InstalledRegionPack(Ffb, "x"));

        Assert.Equal("AKTUALISIEREN", row.ActionLabel);
        Assert.True(row.HasUpdate);
        Assert.False(row.IsCurrent);
    }

    [Fact]
    public void A_row_needs_a_pack() =>
        Assert.Throws<ArgumentException>(() => new MapPackRow(null, null));

    [Fact]
    public void The_progress_text_rounds_the_share_down_and_never_leaves_0_to_100()
    {
        var row = new MapPackRow(Ffb, null) { Progress = 0.419 };
        Assert.Equal("41 % von 16 MB", row.ProgressText);
        row.Progress = 1.5;
        Assert.Equal(100, row.Percent);
        row.Progress = -1;
        Assert.Equal(0, row.Percent);
    }
}
