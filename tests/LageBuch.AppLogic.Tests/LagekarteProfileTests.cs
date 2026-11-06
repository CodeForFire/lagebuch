using LageBuch.AppLogic.Services.Map;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.ValueObjects;

namespace LageBuch.AppLogic.Tests;

/// <summary>The HÖHENPROFIL from a water point's card to the Einsatzort, over <see cref="TerrainFixture"/>'s plane.</summary>
public sealed class LagekarteProfileTests : IDisposable
{
    // 200 m west of the Marktplatz: about 10 m lower on the fixture's plane.
    private static readonly GeoPoint West = new(48.1785, 11.2555 - 0.00269);

    private readonly string _dir = Directory.CreateTempSubdirectory("lagebuch-profile-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Without_an_Einsatzort_the_card_offers_the_profile_but_cannot_draw_it_yet()
    {
        var (_, vm) = Open();
        vm.SelectedWaterPoint = new WaterPoint(West, WaterPointKind.Hydrant, "UH 100");

        Assert.True(vm.OffersProfile);
        Assert.False(vm.ShowProfileCommand.CanExecute(null));
    }

    [Fact]
    public void Without_heights_in_the_packs_the_card_offers_no_profile()
    {
        var library = new FakeRegionPackLibrary();
        library.Preinstall(FakeRegionPackLibrary.Pack("ffb", "Landkreis Fürstenfeldbruck"));
        var (session, _) = Open();
        session.SetIncidentLocation(TerrainFixture.Marktplatz);
        using var vm = new LagekarteViewModel(session, () => { }, library);
        vm.SelectedWaterPoint = new WaterPoint(West, WaterPointKind.Hydrant, "UH 100");

        Assert.False(vm.OffersProfile);
        Assert.False(vm.ShowProfileCommand.CanExecute(null));
    }

    [Fact]
    public void The_profile_runs_from_the_water_point_to_the_Einsatzort_and_says_how_much_higher_it_lies()
    {
        var (session, vm) = Open();
        session.SetIncidentLocation(TerrainFixture.Marktplatz);
        vm.SelectedWaterPoint = new WaterPoint(West, WaterPointKind.Hydrant, "UH 100");

        Assert.True(vm.ShowProfileCommand.CanExecute(null));
        vm.ShowProfileCommand.Execute(null);

        var profile = Assert.IsType<ElevationProfile>(vm.Profile);
        Assert.True(vm.HasProfile);
        Assert.Equal(West, profile.From);
        Assert.Equal(TerrainFixture.Marktplatz, profile.To);
        Assert.Equal("Hydrant → Einsatzort", vm.ProfileTitle);
        Assert.Equal("200 m Luftlinie · Einsatzort 10 m höher · Anstieg gesamt 10 m", vm.ProfileSummary);
        var line = Assert.Single(vm.MapShapes, s => s.Kind == OverlayShapeKind.Line);
        Assert.Equal([West, TerrainFixture.Marktplatz], line.Parts[0]);
    }

    [Fact]
    public void An_Einsatzort_lower_than_the_water_point_is_said_to_lie_lower()
    {
        var (session, vm) = Open();
        session.SetIncidentLocation(West);
        vm.SelectedWaterPoint = new WaterPoint(TerrainFixture.Marktplatz, WaterPointKind.Saugstelle, "Musterbach");

        vm.ShowProfileCommand.Execute(null);

        Assert.Equal("200 m Luftlinie · Einsatzort 10 m tiefer · Anstieg gesamt 0 m", vm.ProfileSummary);
    }

    [Fact]
    public void A_crest_between_the_water_point_and_the_Einsatzort_is_named_with_how_far_along_it_lies()
    {
        // A ridge 20 m high halfway between: the hose has to go over it.
        var (e0, _) = TerrainFixture.MarktplatzUtm;
        var (session, vm) = Open((e, _) => (float)(500 + (20 * Math.Max(0, 1 - (Math.Abs(e - (e0 - 100)) / 50)))));
        session.SetIncidentLocation(TerrainFixture.Marktplatz);
        vm.SelectedWaterPoint = new WaterPoint(West, WaterPointKind.Hydrant, "UH 100");

        vm.ShowProfileCommand.Execute(null);

        // The 5 m samples fall a little either side of the crest, so it reads 19 or 20 m, at about 100 m.
        Assert.True(
            System.Text.RegularExpressions.Regex.IsMatch(vm.ProfileSummary ?? string.Empty, "^200 m Luftlinie · Einsatzort gleich hoch · Anstieg gesamt (19|20) m · höchster Punkt \\+(19|20) m nach (9[5-9]|10[0-5]) m$"),
            vm.ProfileSummary);
        var crest = Assert.Single(vm.MapShapes, s => s.Kind == OverlayShapeKind.Point);
        Assert.Matches("^höchster Punkt \\+(19|20) m$", crest.Label);
    }

    [Fact]
    public void A_plain_climb_names_no_crest()
    {
        var (session, vm) = Open();
        session.SetIncidentLocation(TerrainFixture.Marktplatz);
        vm.SelectedWaterPoint = new WaterPoint(West, WaterPointKind.Hydrant, "UH 100");

        vm.ShowProfileCommand.Execute(null);

        Assert.DoesNotContain("höchster Punkt", vm.ProfileSummary, StringComparison.Ordinal);
        Assert.DoesNotContain(vm.MapShapes, s => s.Kind == OverlayShapeKind.Point);
    }

    [Fact]
    public void Moving_the_Einsatzort_redraws_the_profile_and_removing_it_closes_the_profile()
    {
        var (session, vm) = Open();
        session.SetIncidentLocation(TerrainFixture.Marktplatz);
        vm.SelectedWaterPoint = new WaterPoint(West, WaterPointKind.Hydrant, "UH 100");
        vm.ShowProfileCommand.Execute(null);

        // The card may close meanwhile; the profile keeps its water point.
        vm.CloseWaterPointCommand.Execute(null);
        var further = new GeoPoint(48.1785, 11.2555 + 0.00269);
        session.SetIncidentLocation(further);

        Assert.Equal(further, Assert.IsType<ElevationProfile>(vm.Profile).To);
        Assert.Equal(West, vm.Profile.From);

        session.SetIncidentLocation(null);

        Assert.Null(vm.Profile);
        Assert.False(vm.HasProfile);
        Assert.DoesNotContain(vm.MapShapes, s => s.Kind == OverlayShapeKind.Line);
    }

    [Fact]
    public void Closing_the_profile_takes_its_line_off_the_map()
    {
        var (session, vm) = Open();
        session.SetIncidentLocation(TerrainFixture.Marktplatz);
        vm.SelectedWaterPoint = new WaterPoint(West, WaterPointKind.Hydrant, "UH 100");
        vm.ShowProfileCommand.Execute(null);
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.CloseProfileCommand.Execute(null);

        Assert.Null(vm.Profile);
        Assert.Null(vm.ProfileTitle);
        Assert.Empty(vm.MapShapes);
        Assert.Contains(nameof(LagekarteViewModel.MapShapes), raised);
    }

    [Fact]
    public void A_water_point_outside_the_heights_has_a_profile_that_says_so()
    {
        var (session, vm) = Open();
        session.SetIncidentLocation(TerrainFixture.Marktplatz);
        vm.SelectedWaterPoint = new WaterPoint(new GeoPoint(48.1785, 11.40), WaterPointKind.Hydrant, "UH 100");

        vm.ShowProfileCommand.Execute(null);

        Assert.Equal("An der Entnahmestelle gibt es keine Höhendaten.", vm.ProfileSummary);
    }

    private (LocalIncidentSession Session, LagekarteViewModel Vm) Open(Func<double, double, float>? height = null)
    {
        var library = new FakeRegionPackLibrary();
        var pack = TerrainFixture.Pack(_dir);
        if (height is not null)
        {
            Persistence.Tests.GeoTiffFixtures.Write(pack.ElevationPath, TerrainFixture.Grid with { CellSize = 5, Width = 1200, Height = 1200 }, height, tileSide: 128);
        }

        library.Preinstall(pack);
        var session = TestSession.StartNew(
            new FakeStore(),
            new FixedClock(new DateTimeOffset(2026, 11, 6, 14, 0, 0, TimeSpan.FromHours(1))),
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.lagebuch",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        return (session, new LagekarteViewModel(session, () => { }, library));
    }
}
