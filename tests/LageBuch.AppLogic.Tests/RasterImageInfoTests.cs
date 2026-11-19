using LageBuch.AppLogic.Services.Map;

namespace LageBuch.AppLogic.Tests;

public class RasterImageInfoTests
{
    [Fact]
    public void A_PNG_is_read_by_its_header()
    {
        Assert.Equal(new RasterImageInfo(RasterFormat.Png, 4096, 3333), RasterImageInfo.Read(TestImages.Png(4096, 3333)));
    }

    [Fact]
    public void A_JPEG_is_read_by_its_frame_header_past_the_segments_before_it()
    {
        Assert.Equal(new RasterImageInfo(RasterFormat.Jpeg, 2000, 1000), RasterImageInfo.Read(TestImages.Jpeg(2000, 1000)));
    }

    [Fact]
    public void A_progressive_JPEG_is_read_too()
    {
        var bytes = TestImages.Jpeg(640, 480);
        bytes[21] = 0xC2;

        Assert.Equal(new RasterImageInfo(RasterFormat.Jpeg, 640, 480), RasterImageInfo.Read(bytes));
    }

    [Fact]
    public void Fill_bytes_between_JPEG_segments_are_skipped()
    {
        var jpeg = TestImages.Jpeg(640, 480);
        byte[] padded = [.. jpeg[..20], 0xFF, 0xFF, .. jpeg[20..]];

        Assert.Equal(new RasterImageInfo(RasterFormat.Jpeg, 640, 480), RasterImageInfo.Read(padded));
    }

    [Fact]
    public void A_JPEG_whose_scan_starts_before_any_frame_header_is_unreadable()
    {
        var bytes = TestImages.Jpeg(640, 480);
        bytes[21] = 0xDA;

        Assert.Null(RasterImageInfo.Read(bytes));
    }

    [Fact]
    public void A_segment_running_past_the_end_is_unreadable()
    {
        var bytes = TestImages.Jpeg(640, 480);

        Assert.Null(RasterImageInfo.Read(bytes.AsSpan(0, 26)));
        Assert.Null(RasterImageInfo.Read(bytes.AsSpan(0, 10)));
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(10, 0)]
    [InlineData(-1, 10)]
    public void A_PNG_without_a_size_is_unreadable(int width, int height)
    {
        Assert.Null(RasterImageInfo.Read(TestImages.Png(width, height)));
    }

    [Fact]
    public void A_PNG_whose_first_chunk_is_not_its_header_is_unreadable()
    {
        var bytes = TestImages.Png(10, 10);
        bytes[12] = (byte)'X';

        Assert.Null(RasterImageInfo.Read(bytes));
    }

    [Theory]
    [InlineData("{\"type\": \"FeatureCollection\"}")]
    [InlineData("GIF89a")]
    [InlineData("")]
    public void Anything_else_is_unreadable(string text)
    {
        Assert.Null(RasterImageInfo.Read(System.Text.Encoding.UTF8.GetBytes(text)));
    }

    [Fact]
    public void A_cut_off_PNG_is_unreadable()
    {
        Assert.Null(RasterImageInfo.Read(TestImages.Png(10, 10).AsSpan(0, 20)));
    }
}
