using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;

namespace LageBuch.AppLogic.Tests;

public class MeasurementTabsViewModelTests
{
    private static (CoMessprotokollViewModel Building, MeasurementLogViewModel Log) Modules()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 11, 8, 9, 0, 0, TimeSpan.FromHours(1)));
        var session = TestSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Müller"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        return (new CoMessprotokollViewModel(session, clock, () => { }), new MeasurementLogViewModel(session, clock, () => { }));
    }

    [Theory]
    [InlineData(MeasurementTabsViewModel.GebaeudeTab, MeasurementTabsViewModel.GebaeudeTab)]
    [InlineData(MeasurementTabsViewModel.MessprotokollTab, MeasurementTabsViewModel.MessprotokollTab)]
    [InlineData(-1, MeasurementTabsViewModel.GebaeudeTab)]
    [InlineData(2, MeasurementTabsViewModel.GebaeudeTab)]
    public void A_tab_index_from_outside_the_two_tabs_opens_the_gebaeude_tab(int requested, int expected)
    {
        var (building, log) = Modules();
        using (building)
        using (log)
        {
            Assert.Equal(expected, new MeasurementTabsViewModel(building, log, requested).SelectedTabIndex);
        }
    }

    [Fact]
    public void Both_tabs_learn_that_they_are_on_a_phone_and_back()
    {
        var (building, log) = Modules();
        using (building)
        using (log)
        {
            var tabs = new MeasurementTabsViewModel(building, log);

            tabs.IsNarrow = true;
            Assert.True(building.IsNarrow);
            Assert.True(log.IsNarrow);

            tabs.IsNarrow = false;
            Assert.False(building.IsNarrow);
            Assert.False(log.IsNarrow);
        }
    }
}
