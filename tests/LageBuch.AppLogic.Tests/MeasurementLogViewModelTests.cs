using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.CoMeasurement;
using LageBuch.Domain.Measurements;

namespace LageBuch.AppLogic.Tests;

// #930: the ABC/Gefahrstoff Messprotokoll -- what is typed is read in the view model, every change
// goes through the session, and a closed Einsatz shows its log without letting anything change.
public sealed class MeasurementLogViewModelTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 8, 14, 30, 0, TimeSpan.FromHours(1));

    private static readonly GasDefinition Co = GasDefinition.DefaultCo;

    private static readonly GasDefinition Ex = new("Ex", "%UEG", GasDirection.Rising, 10m, 20m, 40m, 100m);

    private static readonly IReadOnlyList<GasDefinition> Stammdaten = [Co, Ex];

    private static LocalIncidentSession NewSession(FakeStore? store = null, FixedClock? clock = null) =>
        TestSession.StartNew(
            store ?? new FakeStore(),
            clock ?? new FixedClock(T0),
            new SessionOperator("Muster", "FFB 12/1"),
            "/x.lagebuch",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());

    private static MeasurementLogViewModel NewVm(LocalIncidentSession session, FixedClock? clock = null, Action? onChanged = null) =>
        new(session, clock ?? new FixedClock(T0), onChanged ?? (() => { }), Stammdaten);

    private static MeasurementValueInput Input(MeasurementLogViewModel vm, string name) =>
        vm.ValueInputs.Single(i => i.Name == name);

    private static void Record(MeasurementLogViewModel vm, string point, string time, string? co = null, string? ex = null)
    {
        vm.NewPointName = point;
        vm.NewTime = time;
        Input(vm, "CO").Text = co;
        Input(vm, "Ex").Text = ex;
        vm.RecordMeasurementCommand.Execute(null);
        Assert.Null(vm.EntryError);
    }

    [Theory]
    [InlineData("12,5", 12.5, 12.5)]
    [InlineData("12.5", 12.5, 12.5)]
    [InlineData(" 38 ", 38, 38)]
    [InlineData("8–14", 8, 14)]
    [InlineData("8-14", 8, 14)]
    [InlineData("8 — 14", 8, 14)]
    [InlineData("14–8", 8, 14)]
    [InlineData("0,5-1,25", 0.5, 1.25)]
    public void A_reading_is_read_as_a_german_number_or_a_range(string text, double low, double high)
    {
        Assert.True(MeasurementInput.TryParseValue(text, out var value));

        Assert.Equal(new MeasuredValue((decimal)low, (decimal)high), value);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("8–")]
    [InlineData("–8")]
    [InlineData("-3")]
    [InlineData("8–14–20")]
    [InlineData("1.000,5")]
    [InlineData("200000")]
    public void Anything_that_is_not_a_reading_is_refused(string text)
    {
        Assert.False(MeasurementInput.TryParseValue(text, out var value));
        Assert.Null(value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_reading_means_not_measured_and_is_no_error(string? text)
    {
        Assert.True(MeasurementInput.TryParseValue(text, out var value));
        Assert.Null(value);
    }

    [Theory]
    [InlineData("-5,5", -5.5)]
    [InlineData("−12", -12)]
    [InlineData("14", 14)]
    public void A_temperature_may_be_negative(string text, double expected)
    {
        Assert.True(MeasurementInput.TryParseOptionalNumber(text, out var value));
        Assert.Equal((decimal)expected, value);
    }

    [Theory]
    [InlineData("14:05", 14, 5, 0)]
    [InlineData("9:15", 9, 15, 0)]
    [InlineData("1405", 14, 5, 0)]
    [InlineData("14.05", 14, 5, 0)]
    [InlineData("23:55", 23, 55, 0)]
    public void A_time_of_day_lands_on_todays_date(string text, int hour, int minute, int daysBack)
    {
        Assert.True(MeasurementInput.TryParseTime(text, T0, out var at));

        Assert.Equal(new DateTimeOffset(2026, 11, 8 - daysBack, hour, minute, 0, T0.Offset), at);
    }

    [Fact]
    public void A_late_evening_time_entered_after_midnight_is_yesterdays()
    {
        var justAfterMidnight = new DateTimeOffset(2026, 11, 9, 0, 10, 0, TimeSpan.FromHours(1));

        Assert.True(MeasurementInput.TryParseTime("23:55", justAfterMidnight, out var at));

        Assert.Equal(new DateTimeOffset(2026, 11, 8, 23, 55, 0, justAfterMidnight.Offset), at);
    }

    [Theory]
    [InlineData("25:00")]
    [InlineData("14:60")]
    [InlineData("jetzt")]
    [InlineData("")]
    public void A_time_that_is_not_one_is_refused(string text) =>
        Assert.False(MeasurementInput.TryParseTime(text, T0, out _));

    [Fact]
    public void A_fresh_log_offers_an_input_for_every_stammdaten_messgas()
    {
        var vm = NewVm(NewSession());

        Assert.Equal(["CO (ppm)", "Ex (%UEG)"], vm.ValueInputs.Select(i => i.Label));
        Assert.All(vm.ValueInputs, i => Assert.Null(i.ChannelId));
        Assert.Equal("14:30", vm.NewTime);
        Assert.Equal(MeasurementLogPage.Messreihe, vm.SelectedPage);
    }

    [Fact]
    public void Recording_a_messung_at_a_new_point_creates_the_point_writes_through_the_session_and_resets_the_values()
    {
        var session = NewSession();
        var changes = 0;
        var vm = NewVm(session, onChanged: () => changes++);
        vm.NewOrder = "MA 04";
        vm.NewTeam = "Florian Musterstadt 40/1 · Angriffstrupp";
        vm.NewOdour = true;
        vm.NewNote = "stechender Geruch";

        Record(vm, "Kellerabgang", "14:05", co: "38", ex: "8–14");

        var point = Assert.Single(session.Incident.MeasuringPoints);
        Assert.Equal("Kellerabgang", point.Name);
        Assert.Equal(["CO", "Ex"], session.Incident.MeasurementChannels.Select(c => c.Name));
        var measurement = Assert.Single(session.Incident.Measurements);
        Assert.Equal(new DateTimeOffset(2026, 11, 8, 14, 5, 0, T0.Offset), measurement.MeasuredAt);
        Assert.Equal("MA 04", measurement.Order);
        Assert.Equal(point.Id, measurement.PointId);
        Assert.True(measurement.Odour);
        var co = session.Incident.MeasurementChannels[0];
        var ex = session.Incident.MeasurementChannels[1];
        Assert.Equal(MeasuredValue.Single(38m), measurement.Values[co.Id]);
        Assert.Equal(new MeasuredValue(8m, 14m), measurement.Values[ex.Id]);
        Assert.Equal(2, changes); // the Messpunkt, then the Messung

        // What was measured goes; who measured where stays for the next report.
        Assert.All(vm.ValueInputs, i => Assert.Null(i.Text));
        Assert.Equal([co.Id, ex.Id], vm.ValueInputs.Select(i => i.ChannelId));
        Assert.False(vm.NewOdour);
        Assert.Null(vm.NewNote);
        Assert.Equal("Kellerabgang", vm.NewPointName);
        Assert.Equal("Florian Musterstadt 40/1 · Angriffstrupp", vm.NewTeam);
        Assert.Equal("MA 04", vm.NewOrder);

        var row = Assert.Single(vm.Rows);
        Assert.Equal("Kellerabgang #1", row.PointText);
        Assert.Equal("14:05", row.TimeText);
        Assert.Equal("Geruch", row.ObservationText);
        Assert.Equal(["38", "8–14"], row.Cells.Select(c => c.Text));
        Assert.True(row.Cells[0].IsElevated);
        Assert.Equal("unbestätigt", row.StatusText);
    }

    [Fact]
    public void A_bad_value_is_named_under_its_input_and_nothing_is_recorded()
    {
        var session = NewSession();
        var vm = NewVm(session);
        vm.NewPointName = "Kellerabgang";
        Input(vm, "CO").Text = "viel";
        Input(vm, "Ex").Text = "8–14";

        vm.RecordMeasurementCommand.Execute(null);

        Assert.Equal(MeasurementLogViewModel.ValueFormatError, Input(vm, "CO").Error);
        Assert.True(Input(vm, "CO").HasError);
        Assert.Null(Input(vm, "Ex").Error);
        Assert.Equal($"CO: {MeasurementLogViewModel.ValueFormatError}", vm.EntryError);
        Assert.Empty(session.Incident.Measurements);
        Assert.Empty(session.Incident.MeasuringPoints);
        Assert.Equal("viel", Input(vm, "CO").Text); // kept to be fixed, never wiped
    }

    [Fact]
    public void A_messung_that_says_nothing_is_explained_not_recorded()
    {
        var session = NewSession();
        var vm = NewVm(session);
        vm.NewPointName = "Kellerabgang";

        vm.RecordMeasurementCommand.Execute(null);

        Assert.Equal("Messwert, Geruch, Niederschlag oder Bemerkung eintragen", vm.EntryError);
        Assert.Empty(session.Incident.MeasuringPoints);
    }

    [Fact]
    public void A_messung_needs_a_point_and_a_time()
    {
        var vm = NewVm(NewSession());
        Input(vm, "CO").Text = "38";

        vm.RecordMeasurementCommand.Execute(null);
        Assert.Equal("Messpunkt eingeben", vm.EntryError);

        vm.NewPointName = "Kellerabgang";
        vm.NewTime = "gleich";
        vm.RecordMeasurementCommand.Execute(null);
        Assert.Equal("Uhrzeit als SS:MM eingeben, z. B. 14:05", vm.EntryError);
    }

    [Fact]
    public void A_known_point_is_found_whatever_its_case_and_counts_on()
    {
        var session = NewSession();
        var vm = NewVm(session);
        Record(vm, "Kellerabgang", "14:05", co: "38");

        Assert.False(vm.IsNewPoint);
        vm.NewPointName = "kellerabgang ";
        Assert.False(vm.IsNewPoint);
        Record(vm, "kellerabgang ", "14:10", co: "40");

        Assert.Single(session.Incident.MeasuringPoints);
        Assert.Equal(["Kellerabgang #2", "Kellerabgang #1"], vm.Rows.Select(r => r.PointText));
        vm.NewPointName = "Hofeinfahrt";
        Assert.True(vm.IsNewPoint);
    }

    [Fact]
    public void Order_and_team_suggestions_come_from_earlier_messungen_newest_first()
    {
        var vm = NewVm(NewSession());
        vm.NewOrder = "MA 01";
        vm.NewTeam = "Messtrupp 1";
        Record(vm, "Kellerabgang", "14:05", co: "38");
        vm.NewOrder = "MA 02";
        vm.NewTeam = "Messtrupp 2";
        Record(vm, "Kellerabgang", "14:10", co: "40");
        vm.NewOrder = "MA 01";
        Record(vm, "Hofeinfahrt", "14:12", co: "5");

        Assert.Equal(["MA 01", "MA 02"], vm.OrderOptions);
        Assert.Equal(["Messtrupp 2", "Messtrupp 1"], vm.TeamOptions);
        Assert.Equal(["Kellerabgang", "Hofeinfahrt"], vm.PointNameOptions);
    }

    // Under "Alle" a harmless later reading at one Messpunkt must never hide a dangerous one at
    // another: each tile shows the worst of the Messpunkte' latest values.
    [Fact]
    public void The_tiles_show_each_messgroesse_at_its_worst_latest_value_and_the_point_filter_narrows_them()
    {
        var vm = NewVm(NewSession());
        Record(vm, "Kellerabgang", "14:05", co: "38");
        Record(vm, "Hofeinfahrt", "14:10", co: "5", ex: "2");

        Assert.Equal(["38 ppm", "2 %UEG"], vm.Tiles.Select(t => t.ValueText));
        Assert.Equal("Kellerabgang · 14:05", vm.Tiles[0].Footer);
        Assert.Equal("Hofeinfahrt · 14:10", vm.Tiles[1].Footer);

        var keller = vm.PointFilterOptions.Single(o => o.Label == "Kellerabgang");
        vm.NewPointName = null;
        vm.SelectedPointFilter = keller;

        Assert.Equal(["38 ppm", "–"], vm.Tiles.Select(t => t.ValueText));
        Assert.True(vm.Tiles[0].IsElevated);
        Assert.Equal("Erhöht", vm.Tiles[0].SeverityText);
        Assert.Equal("keine Messung", vm.Tiles[1].Footer);
        Assert.Equal("Kellerabgang #1", Assert.Single(vm.Rows).PointText);
        Assert.Equal("Kellerabgang", vm.NewPointName); // the form follows the filter
        Assert.Equal("Alle", vm.PointFilterOptions[0].Label);
    }

    [Fact]
    public void Under_alle_a_tie_in_band_goes_to_the_newer_value_and_a_point_counts_only_with_its_latest()
    {
        var vm = NewVm(NewSession());
        Record(vm, "Kellerabgang", "14:00", co: "250");
        Record(vm, "Kellerabgang", "14:05", co: "4");
        Record(vm, "Hofeinfahrt", "14:10", co: "6");
        Record(vm, "Absperrgrenze", "14:02", co: "7");

        // Kellerabgang's 250 is superseded there; three Normal values remain, the newest wins.
        Assert.Equal("6 ppm", vm.Tiles[0].ValueText);
        Assert.Equal("Hofeinfahrt · 14:10", vm.Tiles[0].Footer);
    }

    // The Verlauf of the mockup: per Messpunkt its values in time order, the Ausreißer kept but
    // marked, so the chart can leave it off the line.
    [Fact]
    public void A_tile_carries_its_verlauf_per_messpunkt_in_time_order()
    {
        var vm = NewVm(NewSession());
        Record(vm, "Kellerabgang", "14:10", co: "8–14");
        Record(vm, "Kellerabgang", "14:05", co: "38");
        Record(vm, "Hofeinfahrt", "14:07", co: "5", ex: "2");
        vm.Rows.Single(r => r.TimeText == "14:07").AssessCommand.Execute(null);
        vm.AssessmentStatus = MeasurementStatus.Outlier;
        vm.AssessmentText = "Gerät nicht genullt";
        vm.ConfirmAssessmentCommand.Execute(null);

        var co = vm.Tiles[0].Trend;
        Assert.Equal("CO", co.Name);
        Assert.Equal("ppm", co.Unit);
        Assert.Equal((Co.Elevated, Co.Dangerous, Co.Lethal), (co.Elevated, co.Dangerous, co.Lethal));
        Assert.Equal(["Kellerabgang", "Hofeinfahrt"], co.Series.Select(s => s.PointName));
        Assert.Equal([(38m, 38m, CoSeverity.Elevated), (8m, 14m, CoSeverity.Normal)], co.Series[0].Points.Select(p => (p.Low, p.High, p.Severity)));
        Assert.Equal(["14:05", "14:10"], co.Series[0].Points.Select(p => Formatting.TimeOfDay(p.At)));
        Assert.True(Assert.Single(co.Series[1].Points).IsOutlier);

        // Ex was measured only at the Hofeinfahrt.
        Assert.Equal("Hofeinfahrt", Assert.Single(vm.Tiles[1].Trend.Series).PointName);
    }

    [Fact]
    public void A_tile_opens_its_verlauf_follows_the_filter_and_closes_again()
    {
        var vm = NewVm(NewSession());
        Record(vm, "Kellerabgang", "14:05", co: "38");
        Record(vm, "Hofeinfahrt", "14:10", co: "5");
        Assert.Null(vm.SelectedTrend);

        vm.Tiles[0].ShowTrendCommand.Execute(null);
        Assert.Equal("CO", vm.SelectedTrend?.Name);
        Assert.True(vm.Tiles[0].IsTrendShown);
        Assert.Equal(2, vm.SelectedTrend!.Series.Count);

        vm.SelectedPointFilter = vm.PointFilterOptions.Single(o => o.Label == "Hofeinfahrt");
        Assert.Equal("Hofeinfahrt", Assert.Single(vm.SelectedTrend!.Series).PointName);
        Assert.True(vm.Tiles[0].IsTrendShown);

        vm.Tiles[0].ShowTrendCommand.Execute(null);
        Assert.Null(vm.SelectedTrend);
        Assert.False(vm.Tiles[0].IsTrendShown);

        vm.Tiles[1].ShowTrendCommand.Execute(null);
        Assert.Equal("Ex", vm.SelectedTrend?.Name);
        vm.CloseTrendCommand.Execute(null);
        Assert.Null(vm.SelectedTrend);
    }

    [Fact]
    public void The_filter_keeps_its_point_across_a_refresh_without_touching_the_form()
    {
        var session = NewSession();
        var vm = NewVm(session);
        Record(vm, "Kellerabgang", "14:05", co: "38");
        vm.SelectedPointFilter = vm.PointFilterOptions[1];
        vm.NewPointName = "Hofeinfahrt";

        session.AddWeatherObservation(T0, WindDirection.W, 12m, 14m, CloudCover.Clear, Precipitation.None, null);

        Assert.Equal("Kellerabgang", vm.SelectedPointFilter?.Label);
        Assert.Equal("Hofeinfahrt", vm.NewPointName);
    }

    [Fact]
    public void An_ausreisser_stays_in_the_list_but_no_longer_counts_for_the_tiles()
    {
        var session = NewSession();
        var vm = NewVm(session);
        Record(vm, "Kellerabgang", "14:05", co: "38");
        Record(vm, "Kellerabgang", "14:10", co: "140");
        Assert.Equal("140 ppm", vm.Tiles[0].ValueText);

        vm.Rows[0].AssessCommand.Execute(null);
        Assert.True(vm.IsAssessmentOpen);
        vm.AssessmentStatus = MeasurementStatus.Outlier;
        vm.AssessmentText = "Gerät nicht genullt";
        vm.ConfirmAssessmentCommand.Execute(null);

        Assert.False(vm.IsAssessmentOpen);
        Assert.Equal("38 ppm", vm.Tiles[0].ValueText);
        Assert.Equal(2, vm.Rows.Count);
        Assert.True(vm.Rows[0].IsOutlier);
        Assert.Equal("Ausreißer", vm.Rows[0].StatusText);
        Assert.Equal("Gerät nicht genullt", vm.Rows[0].AssessmentText);
        Assert.Equal(MeasurementLogViewModel.DefaultAssessor, vm.Rows[0].AssessmentBy);
        Assert.Equal(MeasurementStatus.Outlier, session.Incident.Measurements[1].Status);
    }

    [Fact]
    public void An_assessment_opens_as_bestaetigt_by_the_fachberater_and_needs_a_name()
    {
        var session = NewSession();
        var vm = NewVm(session);
        Record(vm, "Kellerabgang", "14:05", co: "38");

        vm.Rows[0].AssessCommand.Execute(null);
        Assert.Equal(MeasurementStatus.Confirmed, vm.AssessmentStatus);
        Assert.Equal("Fachberater ABC", vm.AssessmentBy);
        Assert.Equal("14:05 · ohne Auftrag · Kellerabgang #1", vm.AssessmentTarget?.Heading);

        vm.AssessmentBy = "  ";
        vm.ConfirmAssessmentCommand.Execute(null);

        Assert.Equal("Bewertet durch: Name eingeben", vm.AssessmentError);
        Assert.True(vm.IsAssessmentOpen);
        Assert.Null(session.Incident.Measurements[0].Assessment);

        vm.CancelAssessmentCommand.Execute(null);
        Assert.False(vm.IsAssessmentOpen);
        Assert.Null(vm.AssessmentError);
    }

    [Fact]
    public void A_correction_needs_a_grund_and_keeps_the_previous_value()
    {
        var session = NewSession();
        var vm = NewVm(session);
        Record(vm, "Kellerabgang", "14:05", co: "140");
        var cell = vm.Rows[0].Cells[0];

        cell.CorrectCommand.Execute(null);
        Assert.True(vm.IsCorrectionOpen);
        Assert.Equal("140", vm.CorrectionValue);
        Assert.Equal("CO (ppm) · bisher: 140", vm.CorrectionHeading);

        vm.ConfirmCorrectionCommand.Execute(null);
        Assert.Equal("Der Messwert ist unverändert.", vm.CorrectionError);

        vm.CorrectionValue = "14";
        vm.ConfirmCorrectionCommand.Execute(null);
        Assert.Equal("Grund eingeben, z. B. Ablesefehler", vm.CorrectionError);
        Assert.Empty(session.Incident.Measurements[0].Corrections);

        vm.CorrectionReason = "Ablesefehler";
        vm.ConfirmCorrectionCommand.Execute(null);

        Assert.False(vm.IsCorrectionOpen);
        var measurement = session.Incident.Measurements[0];
        Assert.Equal(MeasuredValue.Single(14m), measurement.Values[cell.ChannelId]);
        var correction = Assert.Single(measurement.Corrections);
        Assert.Equal(MeasuredValue.Single(140m), correction.Previous);
        Assert.Equal("Ablesefehler", correction.Reason);
        var corrected = vm.Rows[0].Cells[0];
        Assert.True(corrected.IsCorrected);
        Assert.Equal("14", corrected.Text);
        Assert.StartsWith("korrigiert: 140 → 14 (Ablesefehler", corrected.CorrectionText, StringComparison.Ordinal);
    }

    [Fact]
    public void A_correction_to_something_that_is_not_a_reading_is_explained()
    {
        var vm = NewVm(NewSession());
        Record(vm, "Kellerabgang", "14:05", co: "140");
        vm.Rows[0].Cells[0].CorrectCommand.Execute(null);
        vm.CorrectionValue = "vierzehn";
        vm.CorrectionReason = "Ablesefehler";

        vm.ConfirmCorrectionCommand.Execute(null);

        Assert.Equal(MeasurementLogViewModel.ValueFormatError, vm.CorrectionError);
    }

    [Fact]
    public void The_first_messpunkt_copies_the_stammdaten_messgase_in_as_messgroessen()
    {
        var session = NewSession();
        var vm = NewVm(session);
        Assert.Contains("CO, Ex", vm.SeedHint, StringComparison.Ordinal);
        Assert.Empty(vm.GasChoices); // both will arrive with the first Messpunkt

        vm.SelectedPage = MeasurementLogPage.MesspunkteWetter;
        Assert.True(vm.IsMesspunkteWetterPage);
        vm.PointName = "Hofeinfahrt";
        vm.PointLocation = "Industriestraße 7";
        vm.PointZone = MeasurementZone.Cordon;
        vm.SavePointCommand.Execute(null);

        Assert.Null(vm.PointError);
        Assert.Equal(["CO", "Ex"], session.Incident.MeasurementChannels.Select(c => c.Name));
        var row = Assert.Single(vm.Points);
        Assert.Equal("Absperrbereich", row.ZoneText);
        Assert.Equal("0 Messungen", row.CountText);
        Assert.Null(vm.SeedHint);
        Assert.Null(vm.PointName); // the form is ready for the next one
        Assert.Equal(["CO", "Ex"], vm.Channels.Select(c => c.Name));
    }

    [Fact]
    public void Editing_a_point_changes_it_in_place()
    {
        var session = NewSession();
        var vm = NewVm(session);
        vm.PointName = "Hofeinfahrt";
        vm.SavePointCommand.Execute(null);

        vm.Points[0].EditCommand.Execute(null);
        Assert.True(vm.IsEditingPoint);
        Assert.Equal("SPEICHERN", vm.PointSubmitLabel);
        vm.PointName = "Hofeinfahrt Nord";
        vm.SavePointCommand.Execute(null);

        Assert.Equal("Hofeinfahrt Nord", Assert.Single(session.Incident.MeasuringPoints).Name);
        Assert.False(vm.IsEditingPoint);
        Assert.Equal("MESSPUNKT ANLEGEN", vm.PointSubmitLabel);
    }

    [Fact]
    public void A_point_without_a_name_or_with_a_taken_one_is_explained()
    {
        var vm = NewVm(NewSession());
        vm.SavePointCommand.Execute(null);
        Assert.Equal("Messpunkt: Namen eingeben", vm.PointError);

        vm.PointName = "Hofeinfahrt";
        vm.SavePointCommand.Execute(null);
        vm.PointName = "HOFEINFAHRT";
        vm.SavePointCommand.Execute(null);

        Assert.Equal("Messpunkt Hofeinfahrt gibt es schon.", vm.PointError);
    }

    [Fact]
    public void A_stammdaten_messgas_the_einsatz_lacks_is_offered_and_added()
    {
        // The Einsatz was opened with CO alone; the Stammdaten have grown since.
        var session = NewSession();
        session.AddMeasuringPoint("Hofeinfahrt", null, MeasurementZone.Danger, [Co]);
        var gases = new[] { Co, Ex, new GasDefinition("H2S", "ppm", GasDirection.Rising, 5m, 10m, 100m, null) };
        var vm = new MeasurementLogViewModel(session, new FixedClock(T0), () => { }, gases);

        Assert.Equal(["Ex (%UEG)", "H2S (ppm)"], vm.GasChoices.Select(g => g.Label));
        vm.SelectedGasChoice = vm.GasChoices[1];
        vm.AddGasChoiceCommand.Execute(null);

        Assert.Null(vm.ChannelError);
        Assert.Equal(["CO", "H2S"], session.Incident.MeasurementChannels.Select(c => c.Name));
        Assert.Equal(["Ex (%UEG)"], vm.GasChoices.Select(g => g.Label));
        Assert.Null(vm.SelectedGasChoice);
    }

    [Fact]
    public void A_free_messgroesse_follows_the_stammdaten_rules()
    {
        var session = NewSession();
        var vm = NewVm(session);

        vm.AddCustomChannelCommand.Execute(null);
        Assert.Equal("Messgröße: Namen eingeben", vm.ChannelError);

        vm.NewChannelName = "O2";
        vm.NewChannelUnit = "Vol-%";
        vm.NewChannelDirection = GasDirection.Falling;
        vm.NewChannelElevated = 17m;
        vm.NewChannelDangerous = 19m;
        vm.AddCustomChannelCommand.Execute(null);
        Assert.Equal("Schwellen nicht in Reihenfolge", vm.ChannelError);

        vm.NewChannelName = "co";
        vm.NewChannelDangerous = null;
        vm.AddCustomChannelCommand.Execute(null);
        Assert.Equal("Messgröße co gibt es schon.", vm.ChannelError); // CO arrives with the seed

        vm.NewChannelName = "O2";
        vm.AddCustomChannelCommand.Execute(null);

        Assert.Null(vm.ChannelError);
        Assert.Equal(["CO", "Ex", "O2"], session.Incident.MeasurementChannels.Select(c => c.Name));
        var o2 = vm.Channels[2];
        Assert.Equal("fallend", o2.DirectionText);
        Assert.Equal("Erhöht ≤ 17 Vol-%", o2.BandsText);
        Assert.Null(vm.NewChannelName);
    }

    [Fact]
    public void Once_ten_messgroessen_are_there_adding_another_says_so()
    {
        var session = NewSession();
        var vm = NewVm(session);
        for (var i = 1; i <= MeasurementChannel.MaxCount - 2; i++)
        {
            vm.NewChannelName = $"G{i}";
            vm.AddCustomChannelCommand.Execute(null);
            Assert.Null(vm.ChannelError);
        }

        Assert.Equal(MeasurementChannel.MaxCount, session.Incident.MeasurementChannels.Count);
        Assert.True(vm.IsChannelCapReached);

        vm.NewChannelName = "G99";
        vm.AddCustomChannelCommand.Execute(null);

        Assert.Equal(MeasurementLogViewModel.ChannelCapMessage, vm.ChannelError);
        Assert.Equal("Höchstens 10 Messgrößen – eine entfernen oder deaktivieren.", vm.ChannelError);
        Assert.Equal(MeasurementChannel.MaxCount, session.Incident.MeasurementChannels.Count);
    }

    [Fact]
    public void A_messgroesse_with_readings_can_only_be_deactivated_and_then_leaves_the_form()
    {
        var session = NewSession();
        var vm = NewVm(session);
        Record(vm, "Kellerabgang", "14:05", co: "38");
        var co = vm.Channels[0];
        var ex = vm.Channels[1];

        Assert.False(co.RemoveCommand.CanExecute(null));
        Assert.NotNull(co.RemoveHint);
        Assert.True(ex.RemoveCommand.CanExecute(null));

        co.IsActive = false;

        Assert.False(session.Incident.MeasurementChannels[0].IsActive);
        Assert.Equal(["Ex"], vm.ValueInputs.Select(i => i.Name));
        Assert.Equal(["Ex"], vm.Tiles.Select(t => t.Name));
        Assert.Equal(2, vm.Columns.Count); // its column stays, for the values it has

        vm.Channels[1].RemoveCommand.Execute(null);
        Assert.Equal(["CO"], session.Incident.MeasurementChannels.Select(c => c.Name));
        Assert.Equal("Keine aktive Messgröße – unter MESSGRÖSSEN anlegen.", vm.NoChannelsHint);
    }

    [Fact]
    public void A_typed_value_survives_a_change_made_on_another_device()
    {
        var session = NewSession();
        var vm = NewVm(session);
        Record(vm, "Kellerabgang", "14:05", co: "38");
        Input(vm, "Ex").Text = "12";

        session.AddMeasurementChannel(new GasDefinition("H2S", "ppm", GasDirection.Rising, null, null, null, null), null, null);

        Assert.Equal(["CO", "Ex", "H2S"], vm.ValueInputs.Select(i => i.Name));
        Assert.Equal("12", Input(vm, "Ex").Text);
    }

    [Fact]
    public void Devices_are_added_edited_and_removed_and_a_messgroesse_can_name_one()
    {
        var session = NewSession();
        var vm = NewVm(session);
        vm.SelectedPage = MeasurementLogPage.Geraete;
        vm.SaveDeviceCommand.Execute(null);
        Assert.Equal("Gerät: Bezeichnung eingeben", vm.DeviceError);

        vm.NewDeviceKind = DeviceKind.MultiGas;
        vm.NewDeviceName = "X-am 5000";
        vm.NewDeviceNumber = "ARAA-0042";
        vm.NewDeviceVehicle = "GW-G";
        vm.NewDeviceCheck = "Anzeigetest 08:00";
        vm.SaveDeviceCommand.Execute(null);

        var row = Assert.Single(vm.Devices);
        Assert.Equal("Mehrgasmessgerät", row.KindText);
        Assert.Null(vm.NewDeviceName);
        Assert.Equal(["ohne Gerät", "X-am 5000 · Mehrgasmessgerät"], vm.DeviceChoices.Select(d => d.Label));

        vm.NewChannelDevice = vm.DeviceChoices[1];
        vm.NewChannelName = "O2";
        vm.AddCustomChannelCommand.Execute(null);
        Assert.Equal("X-am 5000", vm.Channels.Single(c => c.Name == "O2").DeviceName);

        vm.Devices[0].EditCommand.Execute(null);
        Assert.Equal("SPEICHERN", vm.DeviceSubmitLabel);
        vm.NewDeviceName = "X-am 8000";
        vm.SaveDeviceCommand.Execute(null);
        Assert.Equal("X-am 8000", Assert.Single(session.Incident.MeasuringDevices).Name);

        vm.Devices[0].RemoveCommand.Execute(null);
        Assert.Empty(session.Incident.MeasuringDevices);
        Assert.Null(vm.Channels.Single(c => c.Name == "O2").DeviceName);
    }

    [Fact]
    public void A_removal_asks_first()
    {
        var session = NewSession();
        string? asked = null;
        Action? confirm = null;
        var vm = new MeasurementLogViewModel(session, new FixedClock(T0), () => { }, Stammdaten, (title, _, onConfirmed) =>
        {
            asked = title;
            confirm = onConfirmed;
        });
        vm.NewDeviceName = "X-am 5000";
        vm.SaveDeviceCommand.Execute(null);

        vm.Devices[0].RemoveCommand.Execute(null);
        Assert.Equal("Messgerät entfernen?", asked);
        Assert.Single(session.Incident.MeasuringDevices);

        confirm?.Invoke();
        Assert.Empty(session.Incident.MeasuringDevices);
    }

    [Fact]
    public void An_untouched_uhrzeit_follows_the_clock_and_a_typed_one_is_kept()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock: clock);
        var vm = NewVm(session, clock);
        vm.NewPointName = "Wohnung";
        Input(vm, "CO").Text = "12";

        clock.Now = T0.AddMinutes(40);
        vm.OpenComposerCommand.Execute(null);
        Assert.Equal("15:10", vm.NewTime);
        clock.Now = T0.AddMinutes(45);
        vm.RecordMeasurementCommand.Execute(null);

        Assert.Null(vm.EntryError);
        Assert.Equal(T0.AddMinutes(45), Assert.Single(session.Incident.Measurements).MeasuredAt);

        vm.OpenComposerCommand.Execute(null);
        vm.NewTime = "15:00";
        Input(vm, "CO").Text = "14";
        clock.Now = T0.AddMinutes(60);
        vm.CloseComposerCommand.Execute(null);
        vm.OpenComposerCommand.Execute(null);
        vm.RecordMeasurementCommand.Execute(null);

        Assert.Null(vm.EntryError);
        Assert.Equal(T0.AddMinutes(30), session.Incident.Measurements[1].MeasuredAt);
    }

    [Fact]
    public void An_untouched_wetter_uhrzeit_follows_the_clock()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock: clock);
        var vm = NewVm(session, clock);

        clock.Now = T0.AddMinutes(40);
        vm.AddWeatherCommand.Execute(null);

        Assert.Null(vm.WeatherError);
        Assert.Equal(T0.AddMinutes(40), Assert.Single(session.Incident.WeatherObservations).ObservedAt);
    }

    [Fact]
    public void On_a_joined_device_a_messpunkt_still_on_its_way_says_so_and_keeps_the_values()
    {
        var local = NewSession();
        Incident? beforeTheSnapshot = null;
        var joined = new SnapshotRoundTrippingSession(local, incident => beforeTheSnapshot ??= incident);
        using var vm = new MeasurementLogViewModel(joined, new FixedClock(T0), () => { }, Stammdaten);
        vm.OpenComposerCommand.Execute(null);
        vm.NewPointName = "Kellerabgang";
        Input(vm, "CO").Text = "38";

        vm.RecordMeasurementCommand.Execute(null);

        Assert.Equal("Messpunkt wird angelegt – warten, bis er erscheint, und Messung erneut speichern.", vm.EntryError);
        Assert.Empty(joined.Incident.Measurements);
        Assert.Equal("38", Input(vm, "CO").Text);
        Assert.True(vm.IsComposerOpen);
    }

    [Fact]
    public void Weather_is_recorded_newest_first_from_the_handwindmesser_by_default()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock: clock);
        var vm = NewVm(session, clock);
        Assert.Equal("Handwindmesser", vm.WeatherSource);

        vm.WeatherTime = "14:00";
        vm.WeatherWind = WindDirection.SW;
        vm.WeatherSpeed = "12";
        vm.WeatherTemperature = "-2,5";
        vm.AddWeatherCommand.Execute(null);
        vm.WeatherTime = "14:20";
        vm.WeatherSpeed = "301";
        vm.AddWeatherCommand.Execute(null);
        Assert.Equal("Windgeschwindigkeit: 0 bis 300 km/h", vm.WeatherError);
        vm.WeatherSpeed = null;
        vm.WeatherTemperature = "70";
        vm.AddWeatherCommand.Execute(null);
        Assert.Equal("Temperatur: −60 bis 60 °C", vm.WeatherError);
        vm.WeatherTemperature = null;
        vm.AddWeatherCommand.Execute(null);

        Assert.Null(vm.WeatherError);
        Assert.Equal(2, session.Incident.WeatherObservations.Count);
        Assert.Equal(["14:20", "14:00"], vm.Weather.Select(w => w.TimeText));
        Assert.Equal("SW", vm.Weather[1].WindText);
        Assert.Equal("12 km/h", vm.Weather[1].SpeedText);
        Assert.Equal("-2,5 °C", vm.Weather[1].TemperatureText);
        Assert.Equal("–", vm.Weather[0].SpeedText);
        Assert.Equal("14:30", vm.WeatherTime);
    }

    [Fact]
    public void A_page_is_selected_by_the_segmented_control()
    {
        var vm = NewVm(NewSession());

        vm.SelectPageCommand.Execute(MeasurementLogPage.Messgroessen);

        Assert.True(vm.IsMessgroessenPage);
        Assert.False(vm.IsMessreihePage);
        vm.SelectPageCommand.Execute((MeasurementLogPage)42);
        Assert.Equal(MeasurementLogPage.Messgroessen, vm.SelectedPage);
    }

    [Fact]
    public void A_closed_einsatz_shows_its_log_but_offers_no_way_to_change_it()
    {
        var clock = new FixedClock(T0);
        var store = new FakeStore();
        var seed = NewSession(store, clock);
        var seedVm = NewVm(seed, clock);
        Record(seedVm, "Kellerabgang", "14:05", co: "38");
        seedVm.NewDeviceName = "X-am 5000";
        seedVm.SaveDeviceCommand.Execute(null);
        seed.Close();

        var ro = LocalIncidentSession.OpenReadOnly(store, clock, "/x.lagebuch");
        var vm = NewVm(ro, clock);

        Assert.True(vm.IsReadOnly);
        Assert.False(vm.CanEdit);
        var row = Assert.Single(vm.Rows);
        Assert.False(vm.RecordMeasurementCommand.CanExecute(null));
        Assert.False(vm.SavePointCommand.CanExecute(null));
        Assert.False(vm.AddWeatherCommand.CanExecute(null));
        Assert.False(vm.AddCustomChannelCommand.CanExecute(null));
        Assert.False(vm.AddGasChoiceCommand.CanExecute(null));
        Assert.False(vm.SaveDeviceCommand.CanExecute(null));
        Assert.False(row.AssessCommand.CanExecute(null));
        Assert.False(row.Cells[0].CorrectCommand.CanExecute(null));
        Assert.False(vm.Points[0].EditCommand.CanExecute(null));
        Assert.False(vm.Channels[1].RemoveCommand.CanExecute(null));
        Assert.False(vm.Devices[0].RemoveCommand.CanExecute(null));

        // Inert, not throwing.
        Input(vm, "CO").Text = "40";
        vm.RecordMeasurementCommand.Execute(null);
        row.AssessCommand.Execute(null);
        row.Cells[0].CorrectCommand.Execute(null);
        vm.ConfirmAssessmentCommand.Execute(null);
        vm.ConfirmCorrectionCommand.Execute(null);
        vm.Channels[0].IsActive = false;
        vm.Devices[0].RemoveCommand.Execute(null);

        Assert.Single(ro.Incident.Measurements);
        Assert.False(vm.IsAssessmentOpen);
        Assert.False(vm.IsCorrectionOpen);
        Assert.True(ro.Incident.MeasurementChannels[0].IsActive);
        Assert.Single(ro.Incident.MeasuringDevices);
    }

    [Fact]
    public void A_refusal_from_the_domain_surfaces_in_german()
    {
        var session = NewSession();
        var vm = NewVm(session);
        Record(vm, "Kellerabgang", "14:05", co: "38");
        vm.Rows[0].Cells[0].CorrectCommand.Execute(null);
        vm.CorrectionValue = "14";
        vm.CorrectionReason = "Ablesefehler";

        // Another device takes the Messgröße's value away first.
        session.CorrectMeasurementValue(session.Incident.Measurements[0].Id, vm.Rows[0].Cells[0].ChannelId, MeasuredValue.Single(14m), "Tippfehler");
        vm.ConfirmCorrectionCommand.Execute(null);

        Assert.Equal("Nicht möglich: Der Messwert ist unverändert.", vm.CorrectionError);
        Assert.True(vm.IsCorrectionOpen);
    }

    // The form opens on demand on every width: open for good, its four rows left the Messreihe
    // no room at all on a laptop. A saved Messung closes it again, and a Bewertung or Korrektur
    // takes its place.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_form_opens_on_demand_and_closes_once_the_messung_is_saved(bool narrow)
    {
        var vm = NewVm(NewSession());
        vm.IsNarrow = narrow;
        Assert.False(vm.ShowComposer);
        Assert.True(vm.ShowComposerButton);

        vm.OpenComposerCommand.Execute(null);
        Assert.True(vm.ShowComposer);
        Assert.False(vm.ShowComposerButton);
        Record(vm, "Kellerabgang", "14:05", co: "38");
        Assert.False(vm.IsComposerOpen);
        Assert.False(vm.ShowComposer);
        Assert.True(vm.ShowComposerButton);

        vm.OpenComposerCommand.Execute(null);
        vm.Rows[0].AssessCommand.Execute(null);
        Assert.False(vm.ShowComposer);
        Assert.False(vm.ShowComposerButton);
        vm.CancelAssessmentCommand.Execute(null);
        vm.Rows[0].Cells[0].CorrectCommand.Execute(null);
        Assert.False(vm.ShowComposer);
        vm.CancelCorrectionCommand.Execute(null);
        Assert.True(vm.ShowComposer);

        vm.CloseComposerCommand.Execute(null);
        Assert.False(vm.ShowComposer);
        Assert.True(vm.ShowComposerButton);
    }

    [Fact]
    public void A_closed_einsatz_offers_no_form_on_any_width()
    {
        var session = NewSession();
        session.Close();
        var vm = NewVm(session);

        Assert.False(vm.ShowComposer);
        Assert.False(vm.ShowComposerButton);
        vm.IsNarrow = true;
        Assert.False(vm.ShowComposerButton);
        Assert.False(vm.OpenComposerCommand.CanExecute(null));
    }

    [Fact]
    public void Disposing_stops_following_the_session()
    {
        var session = NewSession();
        var vm = NewVm(session);
        vm.Dispose();

        session.AddMeasuringPoint("Hofeinfahrt", null, MeasurementZone.Danger, Stammdaten);

        Assert.Empty(vm.Points);
    }

    [Fact]
    public void A_negative_number_that_is_not_one_is_refused()
    {
        Assert.False(MeasurementInput.TryParseOptionalNumber("-zwölf", out var value));
        Assert.Null(value);
    }

    [Fact]
    public void A_time_exactly_twelve_hours_ahead_is_still_today()
    {
        var morning = new DateTimeOffset(2026, 11, 8, 11, 0, 0, TimeSpan.FromHours(1));

        Assert.True(MeasurementInput.TryParseTime("23:00", morning, out var at));

        Assert.Equal(new DateTimeOffset(2026, 11, 8, 23, 0, 0, morning.Offset), at);
    }

    [Fact]
    public void Without_stammdaten_messgase_the_form_offers_co_alone()
    {
        using var vm = new MeasurementLogViewModel(NewSession(), new FixedClock(T0), () => { });

        Assert.Equal(["CO (ppm)"], vm.ValueInputs.Select(i => i.Label));
        Assert.Contains("Messgase aus den Stammdaten dazu: CO.", vm.SeedHint, StringComparison.Ordinal);
    }

    [Fact]
    public void The_empty_list_says_whether_nothing_was_measured_at_all_or_only_not_here()
    {
        using var vm = NewVm(NewSession());
        Assert.False(vm.HasRows);
        Assert.Equal("Noch keine Messung.", vm.EmptyText);
        Assert.False(vm.HasWeather);

        Record(vm, "Kellerabgang", "14:05", co: "38");
        Assert.True(vm.HasRows);
        Assert.Null(vm.EmptyText);

        vm.PointName = "Hofeinfahrt";
        vm.SavePointCommand.Execute(null);
        vm.SelectedPointFilter = vm.PointFilterOptions.Single(o => o.Label == "Hofeinfahrt");

        Assert.False(vm.HasRows);
        Assert.Equal("Noch keine Messung an diesem Messpunkt.", vm.EmptyText);
    }

    [Fact]
    public void Each_page_answers_for_itself_alone()
    {
        using var vm = NewVm(NewSession());

        vm.SelectPageCommand.Execute(MeasurementLogPage.Geraete);

        Assert.True(vm.IsGeraetePage);
        Assert.False(vm.IsMessreihePage);
        Assert.False(vm.IsMesspunkteWetterPage);
        Assert.False(vm.IsMessgroessenPage);
        Assert.Equal("GERÄT HINZUFÜGEN", vm.DeviceSubmitLabel);
        Assert.Null(vm.NoChannelsHint);
    }

    [Fact]
    public void A_change_from_the_session_announces_every_list_it_rebuilt()
    {
        var session = NewSession();
        using var vm = NewVm(session);
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        session.AddMeasuringPoint("Hofeinfahrt", null, MeasurementZone.Danger, Stammdaten);

        Assert.Contains(nameof(MeasurementLogViewModel.PointFilterOptions), raised);
        Assert.Contains(nameof(MeasurementLogViewModel.Columns), raised);
        Assert.Contains(nameof(MeasurementLogViewModel.PointNameOptions), raised);
        Assert.Contains(nameof(MeasurementLogViewModel.OrderOptions), raised);
        Assert.Contains(nameof(MeasurementLogViewModel.TeamOptions), raised);
        Assert.Contains(nameof(MeasurementLogViewModel.IsNewPoint), raised);
        Assert.Contains(nameof(MeasurementLogViewModel.HasWeather), raised);
        Assert.Contains(nameof(MeasurementLogViewModel.GasChoices), raised);
        Assert.Contains(nameof(MeasurementLogViewModel.SeedHint), raised);
        Assert.Contains(nameof(MeasurementLogViewModel.IsChannelCapReached), raised);
        Assert.Contains(nameof(MeasurementLogViewModel.DeviceChoices), raised);
        Assert.Contains(nameof(MeasurementLogViewModel.HasRows), raised);
        Assert.Contains(nameof(MeasurementLogViewModel.EmptyText), raised);
        Assert.Contains(nameof(MeasurementLogViewModel.NoChannelsHint), raised);
    }

    [Fact]
    public void A_refresh_replaces_the_lists_instead_of_adding_to_them()
    {
        var session = NewSession();
        using var vm = NewVm(session);
        vm.PointName = "Hofeinfahrt";
        vm.SavePointCommand.Execute(null);
        vm.NewDeviceName = "X-am 5000";
        vm.SaveDeviceCommand.Execute(null);

        session.AddWeatherObservation(T0, WindDirection.W, null, null, CloudCover.Clear, Precipitation.None, null);

        Assert.Single(vm.Points);
        Assert.Single(vm.Devices);
        Assert.Single(vm.Weather);
        Assert.Equal(2, vm.Channels.Count);
    }

    [Fact]
    public void A_closed_einsatz_turns_every_form_away_without_an_error()
    {
        var clock = new FixedClock(T0);
        var store = new FakeStore();
        var seed = NewSession(store, clock);
        using var seedVm = NewVm(seed, clock);
        seedVm.PointName = "Hofeinfahrt";
        seedVm.SavePointCommand.Execute(null);
        seed.Close();
        using var vm = NewVm(LocalIncidentSession.OpenReadOnly(store, clock, "/x.lagebuch"), clock);

        vm.RecordMeasurementCommand.Execute(null);
        vm.PointName = "Kellerabgang";
        vm.SavePointCommand.Execute(null);
        vm.WeatherTime = "gleich";
        vm.AddWeatherCommand.Execute(null);
        vm.AddGasChoiceCommand.Execute(null);
        vm.AddCustomChannelCommand.Execute(null);
        vm.SaveDeviceCommand.Execute(null);

        Assert.Null(vm.EntryError);
        Assert.Null(vm.PointError);
        Assert.Null(vm.WeatherError);
        Assert.Null(vm.ChannelError);
        Assert.Null(vm.DeviceError);
        Assert.Equal("Kellerabgang", vm.PointName);
    }

    [Fact]
    public void A_wetter_uhrzeit_that_is_not_one_is_explained()
    {
        var session = NewSession();
        using var vm = NewVm(session);
        vm.WeatherTime = "gleich";

        vm.AddWeatherCommand.Execute(null);

        Assert.Equal("Uhrzeit als SS:MM eingeben, z. B. 14:05", vm.WeatherError);
        Assert.Empty(session.Incident.WeatherObservations);
    }

    [Fact]
    public void Two_wetter_readings_at_the_same_minute_show_the_later_entered_first()
    {
        var session = NewSession();
        using var vm = NewVm(session);

        session.AddWeatherObservation(T0, WindDirection.W, null, null, CloudCover.Clear, Precipitation.None, null);
        session.AddWeatherObservation(T0, WindDirection.N, null, null, CloudCover.Clear, Precipitation.None, null);

        Assert.Equal(["N", "W"], vm.Weather.Select(w => w.WindText));
    }

    [Fact]
    public void Two_messungen_at_the_same_minute_show_the_later_entered_first()
    {
        using var vm = NewVm(NewSession());
        Record(vm, "Kellerabgang", "14:05", co: "38");
        Record(vm, "Kellerabgang", "14:05", co: "40");

        Assert.Equal(["40", "38"], vm.Rows.Select(r => r.Cells[0].Text));
    }

    [Fact]
    public void Every_bad_value_is_marked_but_the_message_names_the_first()
    {
        var session = NewSession();
        using var vm = NewVm(session);
        vm.NewPointName = "Kellerabgang";
        Input(vm, "CO").Text = "viel";
        Input(vm, "Ex").Text = "wenig";

        vm.RecordMeasurementCommand.Execute(null);

        Assert.Equal($"CO: {MeasurementLogViewModel.ValueFormatError}", vm.EntryError);
        Assert.True(Input(vm, "Ex").HasError);

        Input(vm, "CO").Text = "38";
        Input(vm, "Ex").Text = null;
        vm.RecordMeasurementCommand.Execute(null);

        Assert.Null(vm.EntryError);
        Assert.All(vm.ValueInputs, i => Assert.Null(i.Error));
        Assert.Single(session.Incident.Measurements);
    }

    [Fact]
    public void A_saved_messung_clears_the_niederschlag_too()
    {
        using var vm = NewVm(NewSession());
        vm.NewDeposits = true;

        Record(vm, "Kellerabgang", "14:05");

        Assert.Equal("Ruß/Niederschlag", vm.Rows[0].ObservationText);
        Assert.False(vm.NewDeposits);
        Assert.False(vm.NewOdour);
    }

    [Fact]
    public void A_second_assessment_opens_with_what_the_first_one_said()
    {
        using var vm = NewVm(NewSession());
        Record(vm, "Kellerabgang", "14:05", co: "140");
        vm.Rows[0].AssessCommand.Execute(null);
        vm.AssessmentStatus = MeasurementStatus.Outlier;
        vm.AssessmentBy = "Dr. Beispiel";
        vm.ConfirmAssessmentCommand.Execute(null);
        Assert.True(vm.Rows[0].HasAssessment);
        Assert.False(vm.Rows[0].IsConfirmed);

        vm.Rows[0].AssessCommand.Execute(null);

        Assert.Equal(MeasurementStatus.Outlier, vm.AssessmentStatus);
        Assert.Equal("Dr. Beispiel", vm.AssessmentBy);

        vm.AssessmentStatus = MeasurementStatus.Confirmed;
        vm.ConfirmAssessmentCommand.Execute(null);
        Assert.True(vm.Rows[0].IsConfirmed);
    }

    [Fact]
    public void A_messung_without_assessment_has_none()
    {
        using var vm = NewVm(NewSession());
        vm.NewOrder = "MA 04";
        Record(vm, "Kellerabgang", "14:05", co: "38");

        Assert.False(vm.Rows[0].HasAssessment);
        Assert.False(vm.Rows[0].IsConfirmed);
        Assert.Equal("14:05 · MA 04 · Kellerabgang #1", vm.Rows[0].Heading);
        Assert.Equal("1 Messung", vm.Points[0].CountText);
    }

    [Fact]
    public void A_messgroesse_without_a_unit_is_named_alone()
    {
        var session = NewSession();
        var gases = new[] { Co, new GasDefinition("Rauch", string.Empty, GasDirection.Rising, null, null, null, null) };
        session.AddMeasuringPoint("Hofeinfahrt", null, MeasurementZone.Danger, [Co]);
        using var vm = new MeasurementLogViewModel(session, new FixedClock(T0), () => { }, gases);
        Assert.Equal(["Rauch"], vm.GasChoices.Select(g => g.Label));

        vm.SelectedGasChoice = vm.GasChoices[0];
        vm.AddGasChoiceCommand.Execute(null);
        Assert.Equal(["CO (ppm)", "Rauch"], vm.ValueInputs.Select(i => i.Label));
        vm.NewPointName = "Hofeinfahrt";
        vm.ValueInputs[1].Text = "3";
        vm.RecordMeasurementCommand.Execute(null);

        vm.Rows[0].Cells[1].CorrectCommand.Execute(null);
        Assert.Equal("Rauch · bisher: 3", vm.CorrectionHeading);
        vm.Tiles[1].ShowTrendCommand.Execute(null);
        Assert.Equal("VERLAUF · Rauch", vm.SelectedTrend?.Heading);
        vm.Tiles[0].ShowTrendCommand.Execute(null);
        Assert.Equal("VERLAUF · CO (ppm)", vm.SelectedTrend?.Heading);
    }

    [Fact]
    public void A_chosen_messgas_that_arrives_from_elsewhere_is_no_longer_chosen()
    {
        var session = NewSession();
        session.AddMeasuringPoint("Hofeinfahrt", null, MeasurementZone.Danger, [Co]);
        using var vm = NewVm(session);
        vm.SelectedGasChoice = vm.GasChoices.Single(g => g.Definition.Name == "Ex");

        session.AddMeasurementChannel(Ex, null, null);

        Assert.Null(vm.SelectedGasChoice);
    }

    [Fact]
    public void The_chosen_geraet_stays_chosen_across_a_refresh_and_falls_back_once_it_is_gone()
    {
        var session = NewSession();
        using var vm = NewVm(session);
        vm.NewDeviceName = "X-am 5000";
        vm.SaveDeviceCommand.Execute(null);
        vm.NewDeviceName = "Pac 8000";
        vm.SaveDeviceCommand.Execute(null);
        vm.NewChannelDevice = vm.DeviceChoices[2];

        session.AddWeatherObservation(T0, WindDirection.W, null, null, CloudCover.Clear, Precipitation.None, null);
        Assert.Equal("Pac 8000 · Mehrgasmessgerät", vm.NewChannelDevice?.Label);

        session.RemoveMeasuringDevice(vm.Devices[1].Id);
        Assert.Equal("ohne Gerät", vm.NewChannelDevice?.Label);
    }

    [Fact]
    public void Removing_the_geraet_being_edited_clears_the_form_and_another_one_leaves_it()
    {
        using var vm = NewVm(NewSession());
        vm.NewDeviceName = "X-am 5000";
        vm.SaveDeviceCommand.Execute(null);
        vm.NewDeviceName = "Pac 8000";
        vm.SaveDeviceCommand.Execute(null);

        vm.Devices[0].EditCommand.Execute(null);
        vm.Devices[1].RemoveCommand.Execute(null);
        Assert.True(vm.IsEditingDevice);
        Assert.Equal("X-am 5000", vm.NewDeviceName);

        vm.Devices[0].RemoveCommand.Execute(null);
        Assert.False(vm.IsEditingDevice);
        Assert.Null(vm.NewDeviceName);
    }

    [Fact]
    public void A_closed_verlauf_stays_closed_when_its_messgroesse_comes_back()
    {
        using var vm = NewVm(NewSession());
        Record(vm, "Kellerabgang", "14:05", co: "38", ex: "2");
        vm.Tiles[1].ShowTrendCommand.Execute(null);
        Assert.Equal("Ex", vm.SelectedTrend?.Name);

        vm.Channels[1].IsActive = false;
        Assert.Null(vm.SelectedTrend);
        vm.Channels[1].IsActive = true;

        Assert.Null(vm.SelectedTrend);
        Assert.All(vm.Tiles, t => Assert.False(t.IsTrendShown));
    }

    [Fact]
    public void A_corrected_value_still_counts_as_a_reading_of_its_messgroesse()
    {
        using var vm = NewVm(NewSession());
        Record(vm, "Kellerabgang", "14:05", co: "140", ex: "2");
        vm.Rows[0].Cells[0].CorrectCommand.Execute(null);
        vm.CorrectionValue = "14";
        vm.CorrectionReason = "Ablesefehler";
        vm.ConfirmCorrectionCommand.Execute(null);

        Assert.True(vm.Channels[0].HasReadings);
        Assert.False(vm.Channels[0].RemoveCommand.CanExecute(null));
        Assert.False(vm.Rows[0].Cells[1].IsCorrected);
        Assert.Null(vm.Rows[0].Cells[1].CorrectionText);
    }

    [Fact]
    public void A_messgroesse_without_readings_gives_no_reason_against_removing_it()
    {
        var session = NewSession();
        using var vm = NewVm(session);
        vm.PointName = "Hofeinfahrt";
        vm.SavePointCommand.Execute(null);

        Assert.Null(vm.Channels[0].RemoveHint);
    }

    [Fact]
    public void The_values_typed_before_the_first_messpunkt_follow_their_messgase_into_the_messgroessen()
    {
        var session = NewSession();
        using var vm = NewVm(session);
        Input(vm, "CO").Text = "38";
        Input(vm, "Ex").Text = "12";

        session.AddMeasuringPoint("Hofeinfahrt", null, MeasurementZone.Danger, Stammdaten);

        Assert.All(vm.ValueInputs, i => Assert.NotNull(i.ChannelId));
        Assert.Equal("38", Input(vm, "CO").Text);
        Assert.Equal("12", Input(vm, "Ex").Text);

        session.AddMeasurementChannel(new GasDefinition("H2S", "ppm", GasDirection.Rising, null, null, null, null), null, null);

        Assert.Equal("38", Input(vm, "CO").Text);
        Assert.Equal("12", Input(vm, "Ex").Text);
        Assert.Null(Input(vm, "H2S").Text);
    }

    [Fact]
    public void An_unrelated_change_keeps_the_very_inputs_and_their_errors()
    {
        var session = NewSession();
        using var vm = NewVm(session);
        vm.NewPointName = "Kellerabgang";
        Input(vm, "CO").Text = "viel";
        vm.RecordMeasurementCommand.Execute(null);
        var co = Input(vm, "CO");

        session.AddWeatherObservation(T0, WindDirection.W, null, null, CloudCover.Clear, Precipitation.None, null);

        Assert.Same(co, Input(vm, "CO"));
        Assert.True(co.HasError);
    }

    [Theory]
    [InlineData("ABCDEFGHIJKL", null, null)]
    [InlineData("ABCDEFGHIJKLM", null, "Messgröße: höchstens 12 Zeichen")]
    [InlineData("Gas", "ABCDEFGHIJ", null)]
    [InlineData("Gas", "ABCDEFGHIJK", "Einheit: höchstens 10 Zeichen")]
    public void A_free_messgroesse_name_and_unit_may_be_as_long_as_the_stammdaten_allow(string name, string? unit, string? expected) =>
        Assert.Equal(expected, MeasurementLogViewModel.ValidateChannel(new GasDefinition(name, unit ?? string.Empty, GasDirection.Rising, null, null, null, null)));

    [Fact]
    public void One_threshold_out_of_range_is_enough_to_refuse_a_free_messgroesse() =>
        Assert.StartsWith(
            "Schwellen: 0 bis ",
            MeasurementLogViewModel.ValidateChannel(new GasDefinition("Gas", "ppm", GasDirection.Rising, 10m, 200_000m, null, null)),
            StringComparison.Ordinal);

    [Fact]
    public void A_refused_free_messgroesse_keeps_what_was_typed()
    {
        var session = NewSession();
        using var vm = NewVm(session);
        for (var i = 1; i <= MeasurementChannel.MaxCount - 2; i++)
        {
            vm.NewChannelName = $"G{i}";
            vm.AddCustomChannelCommand.Execute(null);
        }

        vm.NewChannelName = "G99";
        vm.AddCustomChannelCommand.Execute(null);

        Assert.Equal(MeasurementLogViewModel.ChannelCapMessage, vm.ChannelError);
        Assert.Equal("G99", vm.NewChannelName);
    }

    [Fact]
    public void A_free_messgroesse_the_domain_refuses_keeps_what_was_typed()
    {
        var session = NewSession();
        using var vm = NewVm(session);
        vm.NewChannelName = "O2";
        vm.NewChannelElevated = 17m;
        vm.NewChannelDevice = new DeviceChoice(Guid.NewGuid(), "entferntes Gerät");

        vm.AddCustomChannelCommand.Execute(null);

        Assert.NotNull(vm.ChannelError);
        Assert.Equal("O2", vm.NewChannelName);
        Assert.Equal(17m, vm.NewChannelElevated);
        Assert.DoesNotContain(session.Incident.MeasurementChannels, c => c.Name == "O2");
    }

    [Fact]
    public void A_renamed_messpunkt_keeps_the_filter_on_it_without_touching_the_form()
    {
        var session = NewSession();
        using var vm = NewVm(session);
        Record(vm, "Kellerabgang", "14:05", co: "38");
        vm.SelectedPointFilter = vm.PointFilterOptions[1];
        vm.NewPointName = "Hofeinfahrt";
        var point = session.Incident.MeasuringPoints[0];

        session.UpdateMeasuringPoint(point.Id, "Kellertreppe", null, point.Zone);

        Assert.Equal("Kellertreppe", vm.SelectedPointFilter?.Label);
        Assert.Equal("Hofeinfahrt", vm.NewPointName);

        // Picking a Messpunkt afterwards is the Lagebuchführer's choice again, so the form follows.
        vm.SelectedPointFilter = vm.PointFilterOptions[0];
        vm.SelectedPointFilter = vm.PointFilterOptions[1];
        Assert.Equal("Kellertreppe", vm.NewPointName);
    }

    [Fact]
    public void A_tile_and_a_cell_name_the_dangerous_and_the_lethal_band()
    {
        using var vm = NewVm(NewSession());
        Record(vm, "Kellerabgang", "14:05", co: "100");

        Assert.True(vm.Tiles[0].HasValue);
        Assert.True(vm.Tiles[0].IsDangerous);
        Assert.False(vm.Tiles[0].IsLethal);
        Assert.False(vm.Tiles[1].HasValue);
        Assert.True(vm.Rows[0].Cells[0].HasValue);
        Assert.True(vm.Rows[0].Cells[0].IsDangerous);
        Assert.False(vm.Rows[0].Cells[0].IsLethal);
        Assert.False(vm.Rows[0].Cells[1].HasValue);

        Record(vm, "Kellerabgang", "14:10", co: "250");

        Assert.True(vm.Tiles[0].IsLethal);
        Assert.False(vm.Tiles[0].IsDangerous);
        Assert.True(vm.Rows[0].Cells[0].IsLethal);
        Assert.False(vm.Rows[0].Cells[0].IsDangerous);
    }
}
