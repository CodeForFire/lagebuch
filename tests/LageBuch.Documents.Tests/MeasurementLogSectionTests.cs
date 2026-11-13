using LageBuch.Documents.Sections;
using LageBuch.Domain;
using LageBuch.Domain.CoMeasurement;
using LageBuch.Domain.Measurements;
using LageBuch.Domain.Time;
using UglyToad.PdfPig;

namespace LageBuch.Documents.Tests;

// #930: the ABC/Gefahrstoff Messprotokoll in the Bericht.
public class MeasurementLogSectionTests
{
    private static readonly DateTimeOffset Started = new(2026, 11, 8, 14, 0, 0, TimeSpan.FromHours(1));

    private static readonly GasDefinition Ex = new("Ex", "%UEG", GasDirection.Rising, 10m, 20m, 40m, 100m);

    [Fact]
    public void Points_messgroessen_and_geraete_are_listed_with_their_bands_and_device()
    {
        var text = PdfText.Of(IncidentWithLog(), Started, IncidentPdfSections.Messprotokoll);

        PdfText.AssertInOrder(
            text,
            "Messprotokoll",
            "Messpunkte",
            "Messpunkt Ort Bereich Messungen",
            "Kellerabgang Industriestraße 7 Gefahrenbereich 2",
            "Messgrößen",
            "CO (ppm) steigend Erhöht ≥ 20 · Gefährlich ≥ 60 · Lebensgefährlich ≥ 200 ppm",
            "X-am 5000 (Nr. ARAA-0042)",
            "Ex (%UEG)",
            "Messgeräte",
            "Mehrgasmessgerät X-am 5000 ARAA-0042 GW-G");
    }

    [Fact]
    public void The_messreihe_prints_every_messung_in_time_order_with_its_ausreisser_marked_and_its_corrections()
    {
        var text = PdfText.Of(IncidentWithLog(), Started, IncidentPdfSections.Messprotokoll);

        PdfText.AssertInOrder(
            text,
            "Messreihe",
            "Zeit Auftrag Messpunkt Trupp · PSA · Messart Messwerte Status",
            "08.11.2026 14:05",
            "MA 04",
            "Kellerabgang #1",
            "Messtrupp 1",
            "CO 38 ppm (Erhöht) · Ex 8–14 %UEG (Erhöht)",
            "Geruch wahrnehmbar – stechender Geruch",
            "bestätigt",
            "08.11.2026 14:10",
            "Kellerabgang #2",
            "Ausreißer – zählt nicht:",
            "CO 14 ppm",
            "korrigiert: CO 140 ppm → 14 ppm (Grund: Ablesefehler; Muster, 14:15)",
            "Ausreißer");
    }

    [Fact]
    public void The_bewertungen_and_the_weather_follow_the_messreihe()
    {
        var text = PdfText.Of(IncidentWithLog(), Started, IncidentPdfSections.Messprotokoll);

        PdfText.AssertInOrder(
            text,
            "Bewertungen",
            "14:05 Kellerabgang #1: bestätigt – mit zweitem Gerät bestätigt (Fachberater ABC, 08.11.2026 14:20)",
            "14:10 Kellerabgang #2: Ausreißer – Gerät nicht genullt (Fachberater ABC, 08.11.2026 14:20)",
            "Wetter",
            "Zeit Wind aus Geschw. Temp. Bewölkung Niederschlag Quelle",
            "08.11.2026 14:00 SW 12 km/h -2,5 °C klar kein Niederschlag Handwindmesser");
    }

    [Fact]
    public void An_einsatz_that_never_measured_leaves_the_section_out()
    {
        var incident = Incident.Start(new FixedClock(Started), new SessionOperator("Muster"), "Brand");

        var text = PdfText.Of(incident, Started, IncidentPdfSections.All);

        Assert.DoesNotContain("Messprotokoll", text.Replace("Gasmessprotokoll", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public void A_point_without_messungen_says_so_under_the_messreihe()
    {
        var clock = new FixedClock(Started);
        var op = new SessionOperator("Muster");
        var incident = Incident.Start(clock, op, "ABC");
        incident.AddMeasuringPoint(clock, op, "Hofeinfahrt", null, MeasurementZone.Cordon);

        var text = PdfText.Of(incident, Started, IncidentPdfSections.Messprotokoll);

        PdfText.AssertInOrder(text, "Messprotokoll", "Hofeinfahrt — Absperrbereich 0", "Messreihe", "— keine Messung —");
    }

    [Fact]
    public void Unticked_the_section_is_not_printed()
    {
        var text = PdfText.Of(IncidentWithLog(), Started, IncidentPdfSections.All & ~IncidentPdfSections.Messprotokoll);

        Assert.DoesNotContain("Messreihe", text, StringComparison.Ordinal);
    }

    [Fact]
    public void All_includes_the_messprotokoll_section()
    {
        Assert.True(IncidentPdfSections.All.HasFlag(IncidentPdfSections.Messprotokoll));
    }

    [Fact]
    public void Every_table_row_starts_with_all_its_cells_on_one_line()
    {
        // A missing column definition would push a row's last cells onto the next line.
        var lines = PdfText.LinesOf(IncidentWithLog(), Started, IncidentPdfSections.Messprotokoll);

        Assert.Contains("Messgröße Richtung Schwellen Messgerät Bemerkung", lines);
        Assert.Contains("Art Bezeichnung Nr. / Charge Fahrzeug Prüfung", lines);
        Assert.Contains("Kellerabgang Industriestraße 7 Gefahrenbereich 2", lines);
        Assert.Contains("CO (ppm) steigend Erhöht ≥ 20 · Gefährlich ≥ 60 · X-am 5000 (Nr. —", lines);
        Assert.Contains("Mehrgasmessgerät X-am 5000 ARAA-0042 GW-G Anzeigetest 08:00", lines);
        Assert.Contains("08.11.2026 MA 04 Kellerabgang Messtrupp 1 CO 38 ppm (Erhöht) · Ex 8–14 bestätigt", lines);
        Assert.Contains("08.11.2026 SW 12 km/h -2,5 °C klar kein Niederschlag Handwindmesser", lines);
    }

    [Fact]
    public void Any_part_of_the_log_is_enough_to_print_the_section()
    {
        var clock = new FixedClock(Started);
        var op = new SessionOperator("Muster");
        var channelOnly = Incident.Start(clock, op, "ABC");
        channelOnly.AddMeasurementChannel(clock, op, Ex, deviceId: null, note: null);
        var deviceOnly = Incident.Start(clock, op, "ABC");
        deviceOnly.AddMeasuringDevice(DeviceKind.Pid, "PID", null, null, null);
        var weatherOnly = Incident.Start(clock, op, "ABC");
        weatherOnly.AddWeatherObservation(clock, op, Started, WindDirection.W, null, null, CloudCover.Clear, Precipitation.None, null);

        Assert.True(MeasurementLogSection.HasContent(channelOnly));
        Assert.True(MeasurementLogSection.HasContent(deviceOnly));
        Assert.True(MeasurementLogSection.HasContent(weatherOnly));
        Assert.False(MeasurementLogSection.HasContent(Incident.Start(clock, op, "ABC")));
    }

    [Fact]
    public void Only_the_parts_the_log_holds_get_a_heading()
    {
        var clock = new FixedClock(Started);
        var op = new SessionOperator("Muster");
        var weatherOnly = Incident.Start(clock, op, "ABC");
        weatherOnly.AddWeatherObservation(clock, op, Started, WindDirection.W, null, null, CloudCover.Clear, Precipitation.None, null);
        var pointOnly = Incident.Start(clock, op, "ABC");
        pointOnly.AddMeasuringPoint(clock, op, "Hofeinfahrt", null, MeasurementZone.Cordon);

        var weatherText = PdfText.Of(weatherOnly, Started, IncidentPdfSections.Messprotokoll);
        var pointText = PdfText.Of(pointOnly, Started, IncidentPdfSections.Messprotokoll);

        PdfText.AssertInOrder(weatherText, "Messprotokoll", "Messreihe", "— keine Messung —", "Wetter", "W — — klar kein Niederschlag —");
        foreach (var absent in new[] { "Messpunkte", "Messgrößen", "Messgeräte", "Bewertungen" })
        {
            Assert.DoesNotContain(absent, weatherText, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("Messgeräte", pointText, StringComparison.Ordinal);
        Assert.DoesNotContain("Wetter", pointText, StringComparison.Ordinal);
    }

    [Fact]
    public void A_messgroesse_without_unit_or_device_number_and_a_deactivated_one_are_printed_as_such()
    {
        var clock = new FixedClock(Started);
        var op = new SessionOperator("Muster");
        var incident = Incident.Start(clock, op, "ABC");
        var device = incident.AddMeasuringDevice(DeviceKind.Ph, "Indikatorpapier", null, null, null);
        var ph = incident.AddMeasurementChannel(clock, op, new GasDefinition("pH", string.Empty, GasDirection.Rising, null, null, null, null), device.Id, note: "Pfütze", seedGases: [Ex]);
        incident.UpdateMeasurementChannel(incident.MeasurementChannels[0].Id, deviceId: null, isActive: false, note: null);

        var lines = PdfText.LinesOf(incident, Started, IncidentPdfSections.Messprotokoll);

        Assert.Contains("Ex (%UEG) steigend Erhöht ≥ 10 · Gefährlich ≥ 20 · — —", lines);
        Assert.Contains("pH steigend ohne Schwellen Indikatorpapier Pfütze", lines);
        var text = PdfText.Of(incident, Started, IncidentPdfSections.Messprotokoll);
        PdfText.AssertInOrder(text, "Ex (%UEG) deaktiviert steigend", "pH steigend");
        Assert.Equal(1, PdfText.Count(text, "deaktiviert"));
        Assert.True(ph.IsActive);
    }

    [Fact]
    public void A_messung_without_values_says_so_and_lists_what_was_seen_and_an_implausible_value_is_flagged()
    {
        var clock = new FixedClock(Started);
        var op = new SessionOperator("Muster");
        var incident = Incident.Start(clock, op, "ABC");
        var point = incident.AddMeasuringPoint(clock, op, "Hofeinfahrt", null, MeasurementZone.Cordon);
        var co = incident.MeasurementChannels[0];
        incident.RecordMeasurement(
            clock, op, Started.AddMinutes(1), null, point.Id, null, ProtectiveEquipment.None, MeasurementMode.FromVehicle, odour: false, deposits: true, note: null, new Dictionary<Guid, MeasuredValue>());
        incident.RecordMeasurement(
            clock, op, Started.AddMinutes(2), null, point.Id, null, ProtectiveEquipment.None, MeasurementMode.FromVehicle, odour: false, deposits: false, note: null, new Dictionary<Guid, MeasuredValue> { [co.Id] = MeasuredValue.Single(2500m) });

        var text = PdfText.Of(incident, Started, IncidentPdfSections.Messprotokoll);

        PdfText.AssertInOrder(text, "Hofeinfahrt #1", "kein Messwert", "Ruß / Niederschlag erkennbar", "Hofeinfahrt #2", "CO 2500 ppm (Lebensgefährlich)", "(unplausibel?)");
        Assert.DoesNotContain("Geruch", text, StringComparison.Ordinal);
        Assert.Equal(1, PdfText.Count(text, "kein Messwert"));
    }

    [Fact]
    public void A_bewertung_without_a_reason_has_no_dash()
    {
        var clock = new FixedClock(Started);
        var op = new SessionOperator("Muster");
        var incident = Incident.Start(clock, op, "ABC");
        var point = incident.AddMeasuringPoint(clock, op, "Hofeinfahrt", null, MeasurementZone.Cordon);
        var measurement = incident.RecordMeasurement(
            clock, op, Started.AddMinutes(1), null, point.Id, null, ProtectiveEquipment.None, MeasurementMode.Remote, odour: true, deposits: false, note: null, new Dictionary<Guid, MeasuredValue>());
        incident.AssessMeasurement(clock, op, measurement.Id, MeasurementStatus.Confirmed, "Fachberater ABC", text: null);

        var text = PdfText.Of(incident, Started, IncidentPdfSections.Messprotokoll);

        Assert.Contains("• 14:01 Hofeinfahrt #1: bestätigt (Fachberater ABC, 08.11.2026 14:00)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Weather_is_printed_in_time_order_whatever_order_it_was_entered_in()
    {
        var clock = new FixedClock(Started);
        var op = new SessionOperator("Muster");
        var incident = Incident.Start(clock, op, "ABC");
        incident.AddWeatherObservation(clock, op, Started.AddMinutes(30), WindDirection.N, null, null, CloudCover.Overcast, Precipitation.Rain, "DWD");
        incident.AddWeatherObservation(clock, op, Started, WindDirection.S, null, null, CloudCover.Clear, Precipitation.None, "Handwindmesser");

        var text = PdfText.Of(incident, Started, IncidentPdfSections.Messprotokoll);

        PdfText.AssertInOrder(text, "Handwindmesser", "DWD");
    }

    [Fact]
    public void An_ausreisser_is_greyed_out_and_a_value_in_a_band_carries_its_colour()
    {
        var pdf = IncidentPdf.Generate(IncidentWithLog(), Started, sections: IncidentPdfSections.Messprotokoll);

        var grey = TextInColour(pdf, 0x75, 0x75, 0x75);
        var elevated = TextInColour(pdf, 0xFF, 0xB2, 0x3E);

        Assert.Contains("CO14ppm", grey, StringComparison.Ordinal);
        Assert.Contains("Ausreißer", grey, StringComparison.Ordinal);
        Assert.DoesNotContain("bestätigt", grey, StringComparison.Ordinal);
        Assert.DoesNotContain("CO38", grey, StringComparison.Ordinal);
        Assert.Contains("CO38ppm(Erhöht)", elevated, StringComparison.Ordinal);
        Assert.Contains("Ex8–14%UEG(Erhöht)", elevated, StringComparison.Ordinal);
    }

    // Every visible letter drawn in the colour, in drawing order and without spaces.
    private static string TextInColour(byte[] pdf, int red, int green, int blue)
    {
        using var document = PdfDocument.Open(pdf);
        var letters = document.GetPages()
            .SelectMany(page => page.Letters)
            .Where(letter => !string.IsNullOrWhiteSpace(letter.Value) && IsColour(letter.Color.ToRGBValues(), red, green, blue))
            .Select(letter => letter.Value);
        return string.Concat(letters);
    }

    private static bool IsColour((double R, double G, double B) rgb, int red, int green, int blue) =>
        Math.Abs(rgb.R - (red / 255.0)) < 0.01 && Math.Abs(rgb.G - (green / 255.0)) < 0.01 && Math.Abs(rgb.B - (blue / 255.0)) < 0.01;

    private static Incident IncidentWithLog()
    {
        var clock = new FixedClock(Started);
        var op = new SessionOperator("Muster");
        var incident = Incident.Start(clock, op, "ABC");
        var device = incident.AddMeasuringDevice(DeviceKind.MultiGas, "X-am 5000", "ARAA-0042", "GW-G", "Anzeigetest 08:00");
        var point = incident.AddMeasuringPoint(clock, op, "Kellerabgang", "Industriestraße 7", MeasurementZone.Danger, [GasDefinition.DefaultCo, Ex]);
        var co = incident.MeasurementChannels[0];
        var ex = incident.MeasurementChannels[1];
        incident.UpdateMeasurementChannel(co.Id, device.Id, isActive: true, note: null);
        incident.AddWeatherObservation(clock, op, Started, WindDirection.SW, 12m, -2.5m, CloudCover.Clear, Precipitation.None, "Handwindmesser");

        var first = incident.RecordMeasurement(
            clock,
            op,
            Started.AddMinutes(5),
            "MA 04",
            point.Id,
            "Messtrupp 1",
            ProtectiveEquipment.ScbaForm1,
            MeasurementMode.Remote,
            odour: true,
            deposits: false,
            "stechender Geruch",
            new Dictionary<Guid, MeasuredValue> { [co.Id] = MeasuredValue.Single(38m), [ex.Id] = new MeasuredValue(8m, 14m) });
        var second = incident.RecordMeasurement(
            clock,
            op,
            Started.AddMinutes(10),
            "MA 04",
            point.Id,
            "Messtrupp 1",
            ProtectiveEquipment.ScbaForm1,
            MeasurementMode.Remote,
            odour: false,
            deposits: false,
            note: null,
            new Dictionary<Guid, MeasuredValue> { [co.Id] = MeasuredValue.Single(140m) });

        clock.Now = Started.AddMinutes(15);
        incident.CorrectMeasurementValue(clock, op, second.Id, co.Id, MeasuredValue.Single(14m), "Ablesefehler");
        clock.Now = Started.AddMinutes(20);
        incident.AssessMeasurement(clock, op, first.Id, MeasurementStatus.Confirmed, "Fachberater ABC", "mit zweitem Gerät bestätigt");
        incident.AssessMeasurement(clock, op, second.Id, MeasurementStatus.Outlier, "Fachberater ABC", "Gerät nicht genullt");
        return incident;
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now { get; set; } = now;
    }
}
