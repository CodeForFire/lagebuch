namespace LageBuch.Domain.Tests;

// The Fahrzeugkennung is the middle number of a Funkrufname: the 12 in "FFB 1/12/1". It names the
// kind of vehicle, so the Stammdaten can map it to a Fahrzeugtyp once instead of per vehicle.
public class CallSignKennungTests
{
    [Theory]
    [InlineData("FFB 1/12/1", "12")]
    [InlineData("Florian Emmering 1/12/1", "12")]
    [InlineData("Florian Fürstenfeldbruck 2/40/1", "40")]
    [InlineData("1/12/1", "12")]
    [InlineData("FFB 10/44/12", "44")]
    [InlineData("FFB 1/012/1", "012")]
    public void The_kennung_is_the_middle_number(string callSign, string kennung) =>
        Assert.Equal(kennung, CallSignKennung.Of(callSign));

    [Theory]
    [InlineData("  FFB 1/12/1  ")]
    [InlineData(" 1/12/1")]
    [InlineData("FFB 1 / 12 / 1")]
    [InlineData("FFB  1/ 12 /1")]
    [InlineData("FFB\t1/12/1")]
    public void Spacing_around_the_numbers_does_not_matter(string callSign) =>
        Assert.Equal("12", CallSignKennung.Of(callSign));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Florian Musterdorf 40/1")] // two numbers: no middle one to read
    [InlineData("40")]
    [InlineData("Kommandant")]
    [InlineData("FFB 1/12/1/3")] // four numbers is not the shape
    [InlineData("FFB 1/ELW/1")]
    [InlineData("FFB 1/12/a")]
    [InlineData("FFB A/12/1")]
    [InlineData("FFB1/12/1")] // the first number must stand on its own
    [InlineData("FFB 1//1")]
    [InlineData("FFB /12/1")]
    [InlineData("FFB 1/12/")]
    [InlineData("FFB 1/1 2/1")]
    [InlineData("FFB 1/١٢/1")] // digits, but not the ASCII ones a Funkrufname is written in
    public void A_name_without_three_numbers_has_no_kennung(string? callSign) =>
        Assert.Null(CallSignKennung.Of(callSign));
}
