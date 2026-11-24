using LageBuch.Domain.Etb;

namespace LageBuch.Domain.Tests;

// #972: which entries "Messwerte ausblenden" and "Systemmeldungen ausblenden" each hide, and
// which ones the Bericht moves out of its ETB into Messungen.
public class EtbDirectionsTests
{
    [Fact]
    public void A_Gasmessung_is_a_measurement_and_not_a_system_line()
    {
        const string text = "Gasmessung Haus A, EG, Whg. 1: CO 45 ppm";

        Assert.True(EtbDirections.IsMeasurement(EtbDirection.Measurement, text));
        Assert.False(EtbDirections.IsSystem(EtbDirection.Measurement, text));
    }

    // Before #972 a Kartenebene line was written as Measurement; an old file still says so.
    [Theory]
    [InlineData("Kartenebene importiert: prognose (8 Zeitpunkte, 16:00–17:45 Uhr)")]
    [InlineData("Kartenebene entfernt: prognose")]
    public void A_Kartenebene_line_from_an_older_file_reads_as_a_system_line(string text)
    {
        Assert.False(EtbDirections.IsMeasurement(EtbDirection.Measurement, text));
        Assert.True(EtbDirections.IsSystem(EtbDirection.Measurement, text));
    }

    [Theory]
    [InlineData(EtbDirection.System, false, true)]
    [InlineData(EtbDirection.Internal, false, false)]
    [InlineData(EtbDirection.Incoming, false, false)]
    [InlineData(EtbDirection.Outgoing, false, false)]
    public void Only_the_measurement_direction_is_a_measurement(EtbDirection direction, bool measurement, bool system)
    {
        // A human entry that happens to start with the legacy prefix is never moved anywhere.
        Assert.Equal(measurement, EtbDirections.IsMeasurement(direction, "Kartenebene importiert: x"));
        Assert.Equal(system, EtbDirections.IsSystem(direction, "Kartenebene importiert: x"));
        Assert.Equal(measurement, EtbDirections.IsMeasurement(direction, "Lage erhalten"));
        Assert.Equal(system, EtbDirections.IsSystem(direction, "Lage erhalten"));
    }

    [Fact]
    public void The_legacy_prefix_matches_case_sensitively_and_at_the_start_only()
    {
        Assert.True(EtbDirections.IsMeasurement(EtbDirection.Measurement, "kartenebene importiert: x"));
        Assert.True(EtbDirections.IsMeasurement(EtbDirection.Measurement, "Gasmessung Kartenebene x"));
        Assert.True(EtbDirections.IsMeasurement(EtbDirection.Measurement, "Kartenebenen"));
    }
}
