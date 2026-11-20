using System.Net;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.Services.Map;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.ValueObjects;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.Tests;

/// <summary>Closing the Einsatz while LUFTBILD LADEN is under way.</summary>
public sealed class IncidentWorkspaceLuftbildTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 3, 14, 10, 20, 0, TimeSpan.FromHours(1));

    private readonly HttpClient _http = new(new HangingHandler());

    public void Dispose() => _http.Dispose();

    /// <summary>Waits for the caller to give up.</summary>
    private sealed class HangingHandler : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Closing_cancels_a_download_under_way_and_waits_for_it_before_the_Einsatz_closes()
    {
        var clock = new FixedClock(T0);
        var session = TestSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Müller"),
            "/x.lagebuch",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        var vm = new IncidentWorkspaceViewModel(
            session,
            clock,
            new FakeTicker(),
            MasterDataSet.Empty,
            new FakeDialogs(),
            new FakeAlarmService(),
            new NoopIncidentHostController(),
            new TestPdfExporter(),
            luftbild: new LdbvImageryClient(_http));
        var load = vm.Lagekarte.Overlays.LoadLuftbildCommand.ExecuteAsync(new GeoBox(11.2475, 48.1765, 11.2635, 48.1805));

        vm.CloseIncidentCommand.Execute(null);
        var confirm = vm.PendingConfirm;
        Assert.NotNull(confirm);
        confirm.IsOptionChecked = false;
        confirm.ConfirmCommand.Execute(null);

        await load.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(SpinWait.SpinUntil(() => session.Incident.State == IncidentState.Closed, TimeSpan.FromSeconds(5)));
        Assert.Empty(session.Incident.MapOverlays);
    }
}
