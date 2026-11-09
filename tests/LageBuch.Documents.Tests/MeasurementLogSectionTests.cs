using LageBuch.Domain;
using LageBuch.Domain.CoMeasurement;
using LageBuch.Domain.Measurements;
using LageBuch.Domain.Time;

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
