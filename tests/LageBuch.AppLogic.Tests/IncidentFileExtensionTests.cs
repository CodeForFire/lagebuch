using LageBuch.AppLogic.Services;

namespace LageBuch.AppLogic.Tests;

public class IncidentFileExtensionTests
{
    [Theory]
    [InlineData("/home/elw/Einsaetze/20260622-0900.lagebuch")]
    [InlineData(@"C:\Einsaetze\20260622-0900.LAGEBUCH")]
    [InlineData("/home/elw/Einsaetze/uebung.fwincident")]
    [InlineData(@"C:\Einsaetze\Alt.FwIncident")]
    public void Is_incident_file_accepts_lagebuch_and_legacy_fwincident_in_any_case(string path) =>
        Assert.True(IncidentFileExtension.IsIncidentFile(path));

    [Theory]
    [InlineData("/home/elw/bericht.pdf")]
    [InlineData("/home/elw/stammdaten.json")]
    [InlineData("/home/elw/lagebuch")]
    [InlineData("/home/elw/uebung.lagebuch.bak")]
    [InlineData("")]
    public void Is_incident_file_rejects_anything_else(string path) =>
        Assert.False(IncidentFileExtension.IsIncidentFile(path));

    [Fact]
    public void Startup_file_is_the_first_incident_argument()
    {
        var args = new[] { "--verbose", "/home/elw/notizen.txt", "/home/elw/a.lagebuch", "/home/elw/b.fwincident" };

        Assert.Equal("/home/elw/a.lagebuch", IncidentFileExtension.StartupFile(args));
    }

    [Fact]
    public void Startup_file_is_null_without_an_incident_argument()
    {
        Assert.Null(IncidentFileExtension.StartupFile([]));
        Assert.Null(IncidentFileExtension.StartupFile(["--verbose", "/home/elw/bericht.pdf"]));
    }
}
