using System.Text;
using LageBuch.Domain.Files;

namespace LageBuch.Domain.Tests;

// FileNameSanitizer was only exercised through IncidentFile and AppLogic's SafeFileName, so the
// paths neither of them reaches went untested here (#720). Every name below is one a sync peer or a
// content provider could send.
public class FileNameSanitizerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TrySanitize_returns_null_for_a_missing_or_blank_name(string? name)
    {
        Assert.Null(FileNameSanitizer.TrySanitize(name));
    }

    [Theory]
    [InlineData("...")]
    [InlineData("‮")]
    [InlineData("../")]
    public void Sanitize_substitutes_the_fallback_when_nothing_usable_is_left(string hostile)
    {
        Assert.Equal(FileNameSanitizer.DefaultFallback, FileNameSanitizer.Sanitize(hostile));
        Assert.Equal("import.json", FileNameSanitizer.Sanitize(hostile, "import.json"));
    }

    [Fact]
    public void Sanitize_keeps_the_cleaned_name_when_one_is_left()
    {
        Assert.Equal("evil.png", FileNameSanitizer.Sanitize("../../evil.png", "import.json"));
    }

    [Fact]
    public void An_extension_that_fills_the_byte_budget_on_its_own_is_cut_with_the_name()
    {
        // ".xxx…" is exactly 255 bytes, so keeping it would leave no room for a stem and turn the
        // attachment into a hidden dot-file. It is treated as part of the name instead.
        var hostile = "a." + new string('x', 254);

        var sanitized = FileNameSanitizer.TrySanitize(hostile);

        Assert.Equal("a." + new string('x', 253), sanitized);
    }

    [Fact]
    public void An_extension_longer_than_the_byte_budget_does_not_throw()
    {
        var hostile = "a." + new string('x', 300);

        var sanitized = FileNameSanitizer.TrySanitize(hostile);

        Assert.NotNull(sanitized);
        Assert.Equal(255, Encoding.UTF8.GetByteCount(sanitized));
        Assert.StartsWith("a.", sanitized, StringComparison.Ordinal);
    }

    [Fact]
    public void A_long_name_of_emoji_is_cut_between_surrogate_pairs()
    {
        // Each 😀 is a surrogate pair of four UTF-8 bytes; 251 bytes of stem budget is not a
        // multiple of four, so a cut on bytes alone would split one in half.
        var hostile = string.Concat(Enumerable.Repeat("😀", 100)) + ".png";

        var sanitized = FileNameSanitizer.TrySanitize(hostile);

        Assert.NotNull(sanitized);
        Assert.Equal(string.Concat(Enumerable.Repeat("😀", 62)) + ".png", sanitized);
        Assert.DoesNotContain('�', sanitized);
    }
}
