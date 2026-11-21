using LageBuch.Domain.CoMeasurement;
using LageBuch.Domain.Measurements;

namespace LageBuch.Domain.Tests;

public class MeasurementLabelsTests
{
    [Fact]
    public void A_rising_gas_lists_each_band_it_has_with_its_word_and_unit()
    {
        Assert.Equal(
            "Erhöht ≥ 20 · Gefährlich ≥ 60 · Lebensgefährlich ≥ 200 ppm",
            MeasurementLabels.Bands(GasDefinition.DefaultCo));
    }

    [Fact]
    public void A_falling_gas_counts_down_and_a_missing_band_is_skipped()
    {
        var o2 = new GasDefinition("O₂", "Vol-%", GasDirection.Falling, 19m, null, 10.5m, 25m);

        Assert.Equal("Erhöht ≤ 19 · Lebensgefährlich ≤ 10,5 Vol-%", MeasurementLabels.Bands(o2));
    }

    [Fact]
    public void A_gas_without_bands_or_unit_says_so()
    {
        var gas = new GasDefinition("pH", string.Empty, GasDirection.Rising, null, null, null, null);

        Assert.Equal("ohne Schwellen", MeasurementLabels.Bands(gas));
        Assert.Equal("Erhöht ≥ 8", MeasurementLabels.Bands(gas with { Elevated = 8m }));
    }

    [Theory]
    [InlineData(GasDirection.Rising, "steigend")]
    [InlineData(GasDirection.Falling, "fallend")]
    public void The_direction_has_its_german_word(GasDirection direction, string label) =>
        Assert.Equal(label, MeasurementLabels.Direction(direction));

    [Fact]
    public void An_unknown_direction_is_refused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => MeasurementLabels.Direction((GasDirection)7));

    [Theory]
    [InlineData(MeasurementZone.Unknown, "Bereich unbekannt")]
    [InlineData(MeasurementZone.Danger, "Gefahrenbereich")]
    [InlineData(MeasurementZone.Transition, "Übergangsbereich")]
    [InlineData(MeasurementZone.Cordon, "Absperrbereich")]
    [InlineData(MeasurementZone.Outside, "außerhalb")]
    public void Every_zone_has_its_german_word(MeasurementZone zone, string label) =>
        Assert.Equal(label, MeasurementLabels.Zone(zone));

    [Theory]
    [InlineData(ProtectiveEquipment.None, "ohne Sonderausrüstung")]
    [InlineData(ProtectiveEquipment.FilterForm1, "Filter + Form 1")]
    [InlineData(ProtectiveEquipment.ScbaForm1, "PA + Form 1")]
    [InlineData(ProtectiveEquipment.ScbaForm2, "PA + Form 2")]
    [InlineData(ProtectiveEquipment.Form3, "Form 3 (CSA)")]
    public void Every_protection_has_its_german_word(ProtectiveEquipment protection, string label) =>
        Assert.Equal(label, MeasurementLabels.Protection(protection));

    [Theory]
    [InlineData(MeasurementMode.Remote, "abgesetzt (Messtrupp)")]
    [InlineData(MeasurementMode.FromVehicle, "aus dem Fahrzeug")]
    [InlineData(MeasurementMode.Stationary, "Dauermessung (stationär)")]
    public void Every_mode_has_its_german_word(MeasurementMode mode, string label) =>
        Assert.Equal(label, MeasurementLabels.Mode(mode));

    [Theory]
    [InlineData(MeasurementStatus.Unconfirmed, "unbestätigt")]
    [InlineData(MeasurementStatus.Confirmed, "bestätigt")]
    [InlineData(MeasurementStatus.Outlier, "Ausreißer")]
    public void Every_status_has_its_german_word(MeasurementStatus status, string label) =>
        Assert.Equal(label, MeasurementLabels.Status(status));

    [Theory]
    [InlineData(WindDirection.N, "N")]
    [InlineData(WindDirection.NE, "NO")]
    [InlineData(WindDirection.E, "O")]
    [InlineData(WindDirection.SE, "SO")]
    [InlineData(WindDirection.S, "S")]
    [InlineData(WindDirection.SW, "SW")]
    [InlineData(WindDirection.W, "W")]
    [InlineData(WindDirection.NW, "NW")]
    [InlineData(WindDirection.Variable, "umlaufend")]
    [InlineData(WindDirection.Calm, "Windstille")]
    public void Every_wind_direction_is_read_out_in_german(WindDirection wind, string label) =>
        Assert.Equal(label, MeasurementLabels.Wind(wind));

    [Theory]
    [InlineData(CloudCover.Clear, "klar")]
    [InlineData(CloudCover.UpToHalf, "bis 50 % bedeckt")]
    [InlineData(CloudCover.MoreThanHalf, "mehr als 50 % bedeckt")]
    [InlineData(CloudCover.Overcast, "bedeckt")]
    public void Every_cloud_cover_has_its_german_word(CloudCover clouds, string label) =>
        Assert.Equal(label, MeasurementLabels.Clouds(clouds));

    [Theory]
    [InlineData(Precipitation.None, "kein Niederschlag")]
    [InlineData(Precipitation.Rain, "Regen")]
    [InlineData(Precipitation.Snow, "Schnee")]
    [InlineData(Precipitation.Fog, "Nebel")]
    public void Every_precipitation_has_its_german_word(Precipitation precipitation, string label) =>
        Assert.Equal(label, MeasurementLabels.Precipitation(precipitation));

    [Theory]
    [InlineData(DeviceKind.MultiGas, "Mehrgasmessgerät")]
    [InlineData(DeviceKind.SingleGas, "Eingasmessgerät")]
    [InlineData(DeviceKind.TubePump, "Prüfröhrchen / Gasspürpumpe")]
    [InlineData(DeviceKind.TubeAnalyser, "Röhrchen-Analysegerät (digital)")]
    [InlineData(DeviceKind.Pid, "Photoionisationsdetektor (PID)")]
    [InlineData(DeviceKind.DoseRate, "Dosisleistungsmessgerät")]
    [InlineData(DeviceKind.Contamination, "Kontaminationsnachweisgerät")]
    [InlineData(DeviceKind.Ph, "pH-Indikator")]
    [InlineData(DeviceKind.Other, "Sonstiges")]
    public void Every_device_kind_has_its_german_word(DeviceKind kind, string label) =>
        Assert.Equal(label, MeasurementLabels.DeviceKind(kind));

    [Fact]
    public void An_unknown_enum_value_is_refused_by_every_label()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MeasurementLabels.Zone((MeasurementZone)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => MeasurementLabels.Protection((ProtectiveEquipment)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => MeasurementLabels.Mode((MeasurementMode)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => MeasurementLabels.Status((MeasurementStatus)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => MeasurementLabels.Wind((WindDirection)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => MeasurementLabels.Clouds((CloudCover)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => MeasurementLabels.Precipitation((Precipitation)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => MeasurementLabels.DeviceKind((DeviceKind)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => MeasurementLabels.Severity((CoSeverity)99));
    }

    [Fact]
    public void A_value_of_a_messgroesse_without_unit_is_the_bare_number()
    {
        var ph = new MeasurementChannel(Guid.NewGuid(), new GasDefinition("pH", string.Empty, GasDirection.Rising, null, null, null, null), null, true, null);
        var co = new MeasurementChannel(Guid.NewGuid(), GasDefinition.DefaultCo, null, true, null);

        Assert.Equal("7,5", MeasurementLabels.Value(ph, MeasuredValue.Single(7.5m)));
        Assert.Equal("6–8", MeasurementLabels.Value(ph, new MeasuredValue(6m, 8m)));
        Assert.Equal("5 ppm", MeasurementLabels.Value(co, MeasuredValue.Single(5m)));
        Assert.Equal("pH 7,5", MeasurementLabels.NamedValue(ph, MeasuredValue.Single(7.5m)));
    }
}
