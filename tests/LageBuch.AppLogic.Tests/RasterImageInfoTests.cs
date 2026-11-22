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

    // A restart marker has no length. A walk that added one would step over the real frame header
    // and could land on a decoy one, while the decoder reads the real size.
    [Theory]
    [InlineData(0xD0)]
    [InlineData(0xD7)]
    [InlineData(0x01)]
    [InlineData(0xD8)]
    [InlineData(0x00)]
    public void A_length_less_marker_before_the_frame_header_makes_the_JPEG_unreadable(byte marker)
    {
        byte[] bytes = [.. TestImages.Jfif, 0xFF, marker, .. TestImages.Frame(640, 480), .. TestImages.Scan, 0xFF, 0xD9];

        Assert.Null(RasterImageInfo.Read(bytes));
    }

    [Fact]
    public void A_restart_marker_hiding_a_huge_frame_behind_a_small_decoy_is_unreadable()
    {
        // FF D0 followed by two bytes a length-adding walk would read as 0x0013, skipping the
        // 60000 px frame and finding the 64 px one; a decoder reads the first.
        byte[] bytes = [.. TestImages.Jfif, 0xFF, 0xD0, .. TestImages.Frame(60_000, 60_000), .. TestImages.Frame(64, 64), .. TestImages.Scan, 0xFF, 0xD9];

        Assert.Null(RasterImageInfo.Read(bytes));
    }

    [Fact]
    public void Two_frame_headers_before_the_scan_make_the_JPEG_unreadable()
    {
        byte[] bytes = [.. TestImages.Jfif, .. TestImages.Frame(64, 64), .. TestImages.Frame(60_000, 60_000), .. TestImages.Scan, 0xFF, 0xD9];

        Assert.Null(RasterImageInfo.Read(bytes));
    }

    [Fact]
    public void A_JPEG_that_ends_before_its_scan_is_unreadable()
    {
        byte[] bytes = [.. TestImages.Jfif, .. TestImages.Frame(64, 64), 0xFF, 0xD9];

        Assert.Null(RasterImageInfo.Read(bytes));
    }

    [Fact]
    public void A_frame_header_after_another_segment_is_still_found()
    {
        byte[] comment = [0xFF, 0xFE, 0x00, 0x05, (byte)'L', (byte)'D', (byte)'B'];
        byte[] bytes = [.. TestImages.Jfif, .. comment, .. TestImages.Frame(640, 480), .. TestImages.Scan, 0xFF, 0xD9];

        Assert.Equal(new RasterImageInfo(RasterFormat.Jpeg, 640, 480), RasterImageInfo.Read(bytes));
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
