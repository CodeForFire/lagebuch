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
}
