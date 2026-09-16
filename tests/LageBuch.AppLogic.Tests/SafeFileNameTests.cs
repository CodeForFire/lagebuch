using System.Diagnostics.CodeAnalysis;
using LageBuch.AppLogic.Services;

namespace LageBuch.AppLogic.Tests;

[SuppressMessage("Design", "CA1054", Justification = "Tests exercise the sanitiser with free-form (even hostile) strings — that is the point of these tests.")]
public class SafeFileNameTests
{
    [Theory]
    [InlineData("photo.jpg")]
    [InlineData("Kfz-Kennzeichen (1).png")]
    [InlineData("bericht_final.pdf")]
    public void Sanitize_passes_through_an_ordinary_display_name(string name)
    {
        Assert.Equal(name, SafeFileName.Sanitize(name));
    }

    [Theory]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("..\\..\\Windows\\System32\\evil.dll", "evil.dll")]
    [InlineData("/etc/shadow", "shadow")]
    [InlineData("a/b/c.txt", "c.txt")]
    public void Sanitize_reduces_a_path_traversal_attempt_to_its_final_segment(string input, string expected)
    {
        Assert.Equal(expected, SafeFileName.Sanitize(input));
    }

    [Fact]
    public void Sanitize_strips_the_platform_invalid_filename_characters()
    {
        // NUL is invalid on every platform .NET runs on (Windows and Unix both reject it via
        // Path.GetInvalidFileNameChars()), so this assertion holds regardless of CI OS — unlike
        // e.g. ':', which is only invalid on Windows.
        Assert.Equal("foobar.txt", SafeFileName.Sanitize("foo\0bar.txt"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("../..")]
    [InlineData("/")]
    public void Sanitize_falls_back_to_anhang_for_empty_or_dot_only_input(string? input)
    {
        Assert.Equal("anhang", SafeFileName.Sanitize(input));
    }

    [Fact]
    public void Sanitize_falls_back_to_a_caller_supplied_default()
    {
        Assert.Equal("import.json", SafeFileName.Sanitize(null, fallback: "import.json"));
    }
}
