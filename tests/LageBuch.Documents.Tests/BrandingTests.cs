namespace LageBuch.Documents.Tests;

public class BrandingTests
{
    private static readonly byte[] PngHeader = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    /// <summary>
    /// The mark is looked up by a hard-coded manifest resource name, which a rename or a move of
    /// Assets/ would break silently — the header would simply stop showing a logo. Fail here
    /// instead.
    /// </summary>
    [Fact]
    public void Mark_resolves_to_the_embedded_png()
    {
        var mark = Branding.GetMark();

        Assert.True(mark.Length > PngHeader.Length, $"Brand mark unexpectedly small: {mark.Length} bytes.");
        Assert.Equal(PngHeader, mark.Take(PngHeader.Length));
    }

    [Fact]
    public void Mark_is_cached_rather_than_re_read_per_page()
    {
        Assert.Same(Branding.GetMark(), Branding.GetMark());
    }
}
