using System.Diagnostics.CodeAnalysis;
using System.Text;
using LageBuch.AppLogic.Services;
using LageBuch.Domain.Files;

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

    // Everything below is behaviour the Android picked-name path gained by sharing the attachment
    // sanitiser (#302). It previously stripped only Path.GetInvalidFileNameChars(), which on Linux
    // is just NUL and '/'.
    [Theory]
    [InlineData("re<po>rt.pdf", "report.pdf")]
    [InlineData("a:b.png", "ab.png")]
    [InlineData("we|rd?.jpg", "werd.jpg")]
    [InlineData("star*.png", "star.png")]
    public void Sanitize_strips_the_windows_invalid_characters_on_every_platform(string input, string expected)
    {
        // The same picked file can be shared on to a Windows peer, so the stricter set applies here
        // too rather than only where the OS happens to enforce it.
        Assert.Equal(expected, SafeFileName.Sanitize(input));
    }

    [Fact]
    public void Sanitize_strips_invisible_formatting_characters()
    {
        Assert.Equal("Lageplangnp.png", SafeFileName.Sanitize("Lageplan\u202Egnp.png"));
    }

    [Fact]
    public void Sanitize_caps_a_long_name_at_255_bytes_and_keeps_the_extension()
    {
        var sanitized = SafeFileName.Sanitize(new string('a', 300) + ".png");

        Assert.True(Encoding.UTF8.GetByteCount(sanitized) <= 255);
        Assert.Equal(".png", Path.GetExtension(sanitized));
    }

    [Theory]
    [InlineData("...")]
    [InlineData("....")]
    public void Sanitize_falls_back_for_an_all_dots_name(string input)
    {
        // An all-dots name is no more usable than "." or ".." and used to be passed straight through.
        Assert.Equal("anhang", SafeFileName.Sanitize(input));
    }

    [Fact]
    public void Sanitize_trims_surrounding_whitespace()
    {
        Assert.Equal("photo.jpg", SafeFileName.Sanitize("  photo.jpg  "));
    }

    [Fact]
    public void Sanitize_and_the_attachment_sanitiser_agree()
    {
        // The two used to be separate near-duplicates; this is what keeps them from drifting again.
        const string hostile = "..\\..\\Sta<rt>up\\Lageplan\u202Egnp.png";

        Assert.Equal(FileNameSanitizer.TrySanitize(hostile), SafeFileName.Sanitize(hostile));
    }
}
