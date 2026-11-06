namespace LageBuch.Domain.Tests;

public class VehicleSignTests
{
    [Theory]
    [InlineData("LF", VehicleMark.Loeschfahrzeug)]
    [InlineData("HLF", VehicleMark.Loeschfahrzeug)]
    [InlineData("TLF", VehicleMark.Loeschfahrzeug)]
    [InlineData("TSF", VehicleMark.Loeschfahrzeug)]
    [InlineData("SW", VehicleMark.Schlauchwagen)]
    [InlineData("DLK", VehicleMark.Hubrettung)]
    [InlineData("RW", VehicleMark.Ruestwagen)]
    [InlineData("WLF", VehicleMark.Wechsellader)]
    [InlineData("RTW", VehicleMark.Sanitaet)]
    [InlineData("KTW", VehicleMark.Sanitaet)]
    [InlineData("NEF", VehicleMark.Notarzt)]
    [InlineData("ELW 1", VehicleMark.Keines)]
    [InlineData("KdoW", VehicleMark.Keines)]
    [InlineData("GW-L", VehicleMark.Keines)]
    public void Each_catalogue_type_draws_the_sign_of_its_kind(string code, VehicleMark mark)
    {
        Assert.Equal(mark, VehicleTypes.SignFor(code)?.Mark);
    }

    [Theory]
    [InlineData("HLF 20", "HLF")]
    [InlineData("LF 10", "LF")]
    [InlineData("LF20", "LF")]
    [InlineData("TLF 3000", "TLF")]
    [InlineData("DLK 23/12", "DLK")]
    [InlineData("GW-L2", "GW-L")]
    [InlineData("SW 2000", "SW")]
    [InlineData(" rtw ", "RTW")]
    public void A_type_written_out_further_draws_the_sign_of_the_catalogue_type_it_starts_with(string value, string code)
    {
        Assert.Equal(code, VehicleTypes.SignFor(value)?.Code);
    }

    [Theory]
    [InlineData("LFX")]
    [InlineData("DLAK")]
    [InlineData("Boot")]
    [InlineData("")]
    [InlineData(null)]
    public void A_type_the_catalogue_does_not_start_has_no_sign_of_its_own(string? value)
    {
        Assert.Null(VehicleTypes.SignFor(value));
    }

    [Fact]
    public void A_longer_catalogue_code_wins_over_a_shorter_one_it_starts_with()
    {
        // ELW 1 and ELW 2 both start with "ELW"; nothing in the catalogue is "ELW" alone.
        Assert.Equal("ELW 2", VehicleTypes.SignFor("ELW 2")?.Code);
        Assert.Null(VehicleTypes.SignFor("ELW"));
    }

    [Fact]
    public void Loeschfahrzeuge_and_gerätewagen_stand_on_three_wheels_a_car_on_two()
    {
        Assert.True(VehicleTypes.SignFor("HLF")?.MiddleWheel);
        Assert.True(VehicleTypes.SignFor("GW-A")?.MiddleWheel);
        Assert.False(VehicleTypes.SignFor("ELW 1")?.MiddleWheel);
        Assert.False(VehicleTypes.SignFor("RTW")?.MiddleWheel);
    }
}
