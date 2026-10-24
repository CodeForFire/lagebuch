using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.Tests;

public class EtbTabsViewModelTests
{
    private static (EtbViewModel Journal, SituationReportsViewModel Reports) Modules()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2)));
        var session = TestSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Müller"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        return (
            new EtbViewModel(session, clock, MasterDataSet.Empty, () => { }),
            new SituationReportsViewModel(session, () => { }, "ILS"));
    }

    [Theory]
    [InlineData(EtbTabsViewModel.JournalTab, EtbTabsViewModel.JournalTab)]
    [InlineData(EtbTabsViewModel.SituationReportTab, EtbTabsViewModel.SituationReportTab)]
    [InlineData(-1, EtbTabsViewModel.JournalTab)]
    [InlineData(2, EtbTabsViewModel.JournalTab)]
    public void A_tab_index_from_outside_the_two_tabs_opens_the_journal(int requested, int expected)
    {
        var (journal, reports) = Modules();

        Assert.Equal(expected, new EtbTabsViewModel(journal, reports, requested).SelectedTabIndex);
    }

    [Fact]
    public void Both_tabs_learn_that_they_are_on_a_phone_and_back()
    {
        var (journal, reports) = Modules();
        var tabs = new EtbTabsViewModel(journal, reports);

        tabs.IsNarrow = true;
        Assert.True(journal.IsNarrow);
        Assert.True(reports.IsNarrow);

        tabs.IsNarrow = false;
        Assert.False(journal.IsNarrow);
        Assert.False(reports.IsNarrow);
    }
}
