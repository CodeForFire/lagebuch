using LageBuch.AppLogic.Services;

namespace LageBuch.AppLogic.Tests;

public sealed class CrashReportsTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 18, 9, 30, 0, TimeSpan.Zero);

    private readonly string _root = Path.Join(Path.GetTempPath(), $"lagebuch-crash-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    // #300: an exception message can carry a name, an address or a file path -- whatever the code
    // that threw had in hand -- so the report keeps the types and the stack, never the text.
    [Fact]
    public void A_report_names_the_exception_types_and_stack_but_never_a_message()
    {
        var crash = Caught(() => throw new InvalidOperationException(
            "Müller, Hauptstraße 5",
            new IOException("/home/mueller/einsatz.lagebuch")));

        var report = CrashReports.Format(crash, "1.2.3", T0);

        Assert.Contains("1.2.3", report, StringComparison.Ordinal);
        Assert.Contains("System.InvalidOperationException", report, StringComparison.Ordinal);
        Assert.Contains("System.IO.IOException", report, StringComparison.Ordinal);
        Assert.Contains(nameof(A_report_names_the_exception_types_and_stack_but_never_a_message), report, StringComparison.Ordinal);
        Assert.DoesNotContain("Müller", report, StringComparison.Ordinal);
        Assert.DoesNotContain("einsatz.lagebuch", report, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_inner_exception_of_an_aggregate_is_listed()
    {
        var crash = new AggregateException(new TimeoutException("a"), new FormatException("b"));

        var report = CrashReports.Format(crash, "1.2.3", T0);

        Assert.Contains("System.TimeoutException", report, StringComparison.Ordinal);
        Assert.Contains("System.FormatException", report, StringComparison.Ordinal);
    }

    // Written at crash time, announced once on the next start.
    [Fact]
    public void A_written_report_is_announced_once_on_the_next_start()
    {
        Assert.True(CrashReports.TryWrite(_root, new InvalidOperationException("x"), "1.2.3", T0));

        var announced = CrashReports.TakePending(_root);

        Assert.NotNull(announced);
        Assert.True(File.Exists(announced));
        Assert.StartsWith(CrashReports.Folder(_root), announced, StringComparison.Ordinal);
        Assert.Null(CrashReports.TakePending(_root));
    }

    [Fact]
    public void A_start_without_a_crash_announces_nothing() =>
        Assert.Null(CrashReports.TakePending(_root));

    // The folder must not become the log file #300 was rescoped away from.
    [Fact]
    public void Only_the_newest_reports_are_kept()
    {
        for (var i = 0; i < CrashReports.Kept + 3; i++)
        {
            CrashReports.TryWrite(_root, new InvalidOperationException("x"), "1.2.3", T0.AddMinutes(i));
            CrashReports.TakePending(_root);
        }

        Assert.Equal(CrashReports.Kept, Directory.GetFiles(CrashReports.Folder(_root)).Length);
    }

    private static Exception Caught(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is InvalidOperationException)
        {
            return ex;
        }

        throw new InvalidOperationException("nothing thrown");
    }
}
