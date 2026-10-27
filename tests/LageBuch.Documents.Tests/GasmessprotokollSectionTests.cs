using LageBuch.Domain;
using LageBuch.Domain.CoMeasurement;
using LageBuch.Domain.Time;

namespace LageBuch.Documents.Tests;

// #410: the Gasmessprotokoll with several gases -- one Messwerte run per Wohnung, the gas named in
// the Messreihe, and eight gases still on the page.
public class GasmessprotokollSectionTests
{
    private static readonly DateTimeOffset Started = new(2026, 10, 27, 8, 0, 0, TimeSpan.FromHours(1));

    private static readonly GasDefinition H2S = new("H2S", "ppm", GasDirection.Rising, 5m, 10m, 100m, 500m);
    private static readonly GasDefinition O2 = new("O2", "Vol-%", GasDirection.Falling, 19m, 17m, 10m, 25m);

    private sealed class Clock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now { get; set; } = now;
    }

    private static (Incident Incident, Clock Clock, SessionOperator Op) Einsatz(params GasDefinition[] gases)
    {
        var clock = new Clock(Started);
        var op = new SessionOperator("Huber");
        var incident = Incident.Start(clock, op);
        incident.AddCoBuilding(clock, op, "Haus A", 1, 1, gases: gases);
        return (incident, clock, op);
    }

    [Fact]
    public void A_Wohnung_lists_its_Messwerte_with_names_in_list_order()
    {
        var (incident, clock, op) = Einsatz(GasDefinition.DefaultCo, H2S, O2);
        var g = incident.Gases;
        incident.RecordCoValues(clock, op, incident.Buildings[0].Id, 1, 1, new Dictionary<Guid, decimal?> { [g[2].Id] = 20.9m, [g[0].Id] = 120 });

        var text = PdfText.Of(incident, Started.AddHours(1), IncidentPdfSections.CoMessprotokoll);

        Assert.Contains("Whg. 1: CO 120 ppm · O2 20,9 Vol-%, Schlüssel", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Betroffene_Wohnungen_carry_the_Messwerte()
    {
        var (incident, clock, op) = Einsatz(GasDefinition.DefaultCo, H2S);
        var haus = incident.Buildings[0].Id;
        incident.RecordCoValues(clock, op, haus, 0, 1, new Dictionary<Guid, decimal?> { [incident.Gases[1].Id] = 12 });
        incident.SetDwellingStatus(clock, op, haus, 0, 1, DwellingStatus.Affected);

        var text = PdfText.Of(incident, Started.AddHours(1), IncidentPdfSections.CoMessprotokoll);

        Assert.Contains("Schlüssel: —, Messwerte: H2S 12 ppm", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_Messreihe_names_the_gas_of_each_reading()
    {
        var (incident, clock, op) = Einsatz(GasDefinition.DefaultCo, H2S);
        var haus = incident.Buildings[0].Id;
        incident.RecordCoValues(clock, op, haus, 0, 1, new Dictionary<Guid, decimal?> { [incident.Gases[0].Id] = 120, [incident.Gases[1].Id] = 3 });
        clock.Now = Started.AddMinutes(27);
        incident.RecordCoValues(clock, op, haus, 0, 1, new Dictionary<Guid, decimal?> { [incident.Gases[0].Id] = 40 });

        var text = PdfText.Of(incident, Started.AddHours(1), IncidentPdfSections.CoMessprotokoll);

        Assert.Contains("Messreihe: 08:00 CO 120 ppm · 08:00 H2S 3 ppm · 08:27 CO 40 ppm", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Eight_gases_all_print_on_one_Wohnung()
    {
        var gases = Enumerable.Range(1, 8).Select(i => H2S with { Name = $"GAS{i}" }).ToArray();
        var (incident, clock, op) = Einsatz(gases);
        var values = incident.Gases.ToDictionary(g => g.Id, g => (decimal?)(100 + incident.Gases.ToList().IndexOf(g)));
        incident.RecordCoValues(clock, op, incident.Buildings[0].Id, 0, 1, values);

        var text = PdfText.Of(incident, Started.AddHours(1), IncidentPdfSections.CoMessprotokoll);

        PdfText.AssertInOrder(text, Enumerable.Range(1, 8).Select(i => $"GAS{i} {99 + i} ppm").ToArray());
    }
}
