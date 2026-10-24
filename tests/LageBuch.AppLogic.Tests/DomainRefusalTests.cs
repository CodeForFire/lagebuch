using LageBuch.AppLogic.ViewModels;

namespace LageBuch.AppLogic.Tests;

public class DomainRefusalTests
{
    [Fact]
    public void An_argument_refusal_loses_the_English_parameter_suffix()
    {
        var ex = new ArgumentException("Telefon ist länger als das Limit von 50 Zeichen.", "value");

        Assert.Equal("Nicht möglich: Telefon ist länger als das Limit von 50 Zeichen.", DomainRefusal.Message(ex));
    }

    [Fact]
    public void An_out_of_range_refusal_loses_the_suffix_and_the_actual_value()
    {
        var ex = new ArgumentOutOfRangeException("slot", 7, "Unbekannter Posten.");

        Assert.Equal("Nicht möglich: Unbekannter Posten.", DomainRefusal.Message(ex));
    }

    [Fact]
    public void An_argument_refusal_without_a_parameter_name_is_shown_as_it_is()
    {
        Assert.Equal("Nicht möglich: Name fehlt.", DomainRefusal.Message(new ArgumentException("Name fehlt.")));
    }

    [Fact]
    public void Any_other_refusal_is_shown_as_it_is()
    {
        Assert.Equal(
            "Nicht möglich: Abschnitt ist aufgelöst. (Parameter 'x')",
            DomainRefusal.Message(new InvalidOperationException("Abschnitt ist aufgelöst. (Parameter 'x')")));
    }
}
