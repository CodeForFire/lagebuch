using LageBuch.AppLogic.Services.Dispatch;

namespace LageBuch.AppLogic.Tests;

public sealed class JsonDispatchInboxStoreTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 8, 17, 42, 0, TimeSpan.FromHours(1));

    private readonly string _path = Path.Join(Path.GetTempPath(), $"dispatch-inbox-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        File.Delete(_path);
        File.Delete(_path + ".tmp");
    }

    [Fact]
    public void A_fresh_store_has_no_folder_and_nothing_answered()
    {
        var store = new JsonDispatchInboxStore(_path);

        Assert.Null(store.GetFolder());
        Assert.Empty(store.GetHandled());
    }

    [Fact]
    public void The_folder_survives_a_restart_and_can_be_cleared()
    {
        new JsonDispatchInboxStore(_path).SetFolder("/onedrive/Alarm");
        Assert.Equal("/onedrive/Alarm", new JsonDispatchInboxStore(_path).GetFolder());

        new JsonDispatchInboxStore(_path).SetFolder(null);
        Assert.Null(new JsonDispatchInboxStore(_path).GetFolder());
    }

    [Fact]
    public void An_answered_alarm_survives_a_restart_and_keeps_the_folder()
    {
        new JsonDispatchInboxStore(_path).SetFolder("/onedrive/Alarm");
        new JsonDispatchInboxStore(_path).MarkHandled("T 1|x", T0);

        var store = new JsonDispatchInboxStore(_path);
        Assert.Contains("T 1|x", store.GetHandled());
        Assert.Equal("/onedrive/Alarm", store.GetFolder());
    }

    [Fact]
    public void Answers_older_than_the_retention_are_dropped_on_the_next_answer()
    {
        var store = new JsonDispatchInboxStore(_path);
        store.MarkHandled("alt", T0);
        store.MarkHandled("neu", T0 + JsonDispatchInboxStore.Retention + TimeSpan.FromMinutes(1));

        Assert.Equal(["neu"], store.GetHandled());
    }

    [Fact]
    public void A_blank_folder_switches_alarm_import_off()
    {
        var store = new JsonDispatchInboxStore(_path);
        store.SetFolder("  ");

        Assert.Null(store.GetFolder());
    }

    [Fact]
    public void An_answer_exactly_as_old_as_the_retention_is_dropped()
    {
        var store = new JsonDispatchInboxStore(_path);
        store.MarkHandled("alt", T0);
        store.MarkHandled("neu", T0 + JsonDispatchInboxStore.Retention);

        Assert.Equal(["neu"], store.GetHandled());
    }

    [Fact]
    public void A_corrupt_file_reads_as_empty()
    {
        File.WriteAllText(_path, "{ kaputt");

        Assert.Null(new JsonDispatchInboxStore(_path).GetFolder());
    }
}
