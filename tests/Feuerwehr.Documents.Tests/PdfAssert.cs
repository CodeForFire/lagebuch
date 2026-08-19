using System.Text;
using System.Text.RegularExpressions;

namespace Feuerwehr.Documents.Tests;

public static class PdfAssert
{
    private static readonly byte[] PdfHeader = { 0x25, 0x50, 0x44, 0x46, 0x2D }; // "%PDF-"

    public static void IsPdf(byte[] bytes)
    {
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 100, $"PDF unexpectedly small: {bytes.Length} bytes.");
        for (var i = 0; i < PdfHeader.Length; i++)
            Assert.Equal(PdfHeader[i], bytes[i]);
    }

    /// <summary>
    /// Counts page objects via their raw <c>/Type /Page</c> dictionary entries (excluding
    /// <c>/Type /Pages</c>, the page-tree node). A regex over the raw bytes rather than a real PDF
    /// parser — good enough to assert "the merge added N pages" without a parsing dependency.
    /// </summary>
    public static int CountPages(byte[] bytes) =>
        Regex.Matches(Encoding.Latin1.GetString(bytes), @"/Type\s*/Page(?!s)").Count;
}
