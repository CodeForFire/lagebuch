using LageBuch.AppLogic.Services.Dispatch;

namespace LageBuch.AppLogic.Tests;

public sealed class DispatchFolderScannerTests : IDisposable
{
    private static readonly TimeZoneInfo Cet = TimeZoneInfo.CreateCustomTimeZone("Test/CET", TimeSpan.FromHours(1), "CET", "CET");

    // 25 minutes after the sample's 17:17 alarm: the ELW has arrived and its PC has just booted.
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 17, 42, 0, TimeSpan.FromHours(1));

    private static readonly IReadOnlySet<string> NoneHandled = new HashSet<string>(StringComparer.Ordinal);

    private readonly string _folder = Directory.CreateTempSubdirectory("dispatch-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private DispatchScan Scan(IReadOnlySet<string>? handled = null) =>
        new DispatchFolderScanner(new FixedClock(Now), Cet).Scan(_folder, handled ?? NoneHandled);

    private string Drop(string name, string content, DateTimeOffset? written = null)
    {
        var path = Path.Join(_folder, name);
        File.WriteAllText(path, content);
        File.SetLastWriteTimeUtc(path, (written ?? Now.AddMinutes(-24)).UtcDateTime);
        return path;
    }

    [Fact]
    public void An_alarm_mail_from_before_the_pc_was_started_is_offered()
    {
        Drop("alarm.html", DispatchSamples.Html());

        var scan = Scan();

        Assert.Null(scan.Error);
        Assert.Equal(DispatchSamples.IncidentNumber, scan.Newest?.IncidentNumber);
    }

    [Fact]
    public void An_alarm_older_than_two_hours_is_not_offered()
    {
        Drop("alarm.html", DispatchSamples.Html(DispatchSamples.Xml(alarmedAt: "2026-11-08 15:30")), written: Now.AddMinutes(-20));

        Assert.Null(Scan().Newest);
    }

    [Fact]
    public void A_file_last_written_over_two_hours_ago_is_not_read()
    {
        Drop("alarm.html", DispatchSamples.Html(), written: Now.AddHours(-3));

        Assert.Null(Scan().Newest);
    }

    [Fact]
    public void Without_an_alarm_time_the_file_time_decides_the_age()
    {
        Drop("alarm.xml", DispatchSamples.Xml(alarmedAt: string.Empty), written: Now.AddMinutes(-5));

        Assert.NotNull(Scan().Newest);
    }

    [Theory]
    [InlineData("2026-11-08 15:42")] // exactly two hours before
    [InlineData("2026-11-08 17:57")] // a Leitstelle clock a quarter of an hour ahead
    public void An_alarm_at_the_edge_of_the_window_is_still_offered(string alarmedAt)
    {
        Drop("alarm.html", DispatchSamples.Html(DispatchSamples.Xml(alarmedAt: alarmedAt)), written: Now.AddMinutes(-1));

        Assert.NotNull(Scan().Newest);
    }

    [Fact]
    public void An_alarm_stamped_further_in_the_future_is_not_offered()
    {
        Drop("alarm.html", DispatchSamples.Html(DispatchSamples.Xml(alarmedAt: "2026-11-08 18:30")), written: Now.AddMinutes(-1));

        Assert.Null(Scan().Newest);
    }

    // A file the sync client still holds is passed over until the next poll, and the rest is read.
    [Fact]
    public void An_unreadable_file_is_passed_over_and_the_others_still_read()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // no Unix file mode to take the read permission away with
        }

        var locked = Drop("a.html", DispatchSamples.Html(DispatchSamples.Xml(incidentNumber: "T 9", alarmedAt: "2026-11-08 17:40")));
        File.SetUnixFileMode(locked, UnixFileMode.None);
        Drop("b.html", DispatchSamples.Html());

        try
        {
            Assert.Equal(DispatchSamples.IncidentNumber, Scan().Newest?.IncidentNumber);
        }
        finally
        {
            File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    // A link reports its own size, not what it points at: /dev/zero is "0 bytes" and never ends.
    [Fact]
    public void A_link_in_the_folder_is_not_followed()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // creating a symbolic link needs Developer Mode or admin rights there
        }

        var target = Path.Join(Directory.CreateTempSubdirectory("dispatch-target-").FullName, "alarm.html");
        File.WriteAllText(target, DispatchSamples.Html());
        File.CreateSymbolicLink(Path.Join(_folder, "alarm.html"), target);

        try
        {
            Assert.Null(Scan().Newest);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(target)!, recursive: true);
        }
    }

    // The size on disk is read before the file is; one that grows in between is still cut off.
    [Fact]
    public void A_file_longer_than_the_cap_when_read_is_refused()
    {
        using var grown = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(DispatchSamples.Html() + new string(' ', (int)DispatchFolderScanner.MaxFileBytes)));

        Assert.False(DispatchFolderScanner.TryReadCapped(grown, out _));
    }

    [Fact]
    public void A_file_right_at_the_cap_is_read_whole()
    {
        var html = DispatchSamples.Html();
        var padded = html + new string(' ', (int)DispatchFolderScanner.MaxFileBytes - System.Text.Encoding.UTF8.GetByteCount(html));
        using var exact = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(padded));

        Assert.True(DispatchFolderScanner.TryReadCapped(exact, out var content));
        Assert.Equal(padded, content);
    }

    [Fact]
    public void An_answered_alarm_is_not_offered_again()
    {
        Drop("alarm.html", DispatchSamples.Html());
        var key = Scan().Newest!.Key;

        Assert.Null(Scan(new HashSet<string>(StringComparer.Ordinal) { key }).Newest);
    }

    [Fact]
    public void The_most_recent_alarm_wins_whatever_the_file_order()
    {
        Drop("b.html", DispatchSamples.Html(DispatchSamples.Xml(incidentNumber: "T 1", alarmedAt: "2026-11-08 17:17")), written: Now.AddMinutes(-1));
        Drop("a.html", DispatchSamples.Html(DispatchSamples.Xml(incidentNumber: "T 2", alarmedAt: "2026-11-08 17:35")), written: Now.AddMinutes(-6));

        Assert.Equal("T 2", Scan().Newest?.IncidentNumber);
    }

    [Fact]
    public void Files_that_are_not_alarms_are_passed_over()
    {
        Drop("newsletter.html", "<html><body>Dienstplan November</body></html>");
        Drop("notes.txt", DispatchSamples.Html());

        Assert.Null(Scan().Newest);
    }

    [Fact]
    public void A_file_over_the_size_cap_is_not_read()
    {
        Drop("alarm.html", DispatchSamples.Html() + new string(' ', (int)DispatchFolderScanner.MaxFileBytes));

        Assert.Null(Scan().Newest);
    }

    [Fact]
    public void A_missing_folder_is_reported_not_thrown()
    {
        var scan = new DispatchFolderScanner(new FixedClock(Now), Cet).Scan(Path.Join(_folder, "fehlt"), NoneHandled);

        Assert.Null(scan.Newest);
        Assert.StartsWith("Alarm-Ordner nicht lesbar", scan.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Scanning_leaves_the_files_where_they_are()
    {
        var path = Drop("alarm.html", DispatchSamples.Html());

        _ = Scan();

        Assert.True(File.Exists(path));
    }
}
