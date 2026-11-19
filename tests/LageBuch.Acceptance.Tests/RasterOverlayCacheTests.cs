using System.Buffers.Binary;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using LageBuch.App.Shared.Controls;
using LageBuch.AppLogic.Services.Map;
using LageBuch.Domain.ValueObjects;

namespace LageBuch.Acceptance.Tests;

/// <summary>
/// The decoder's own word on a picture's size, asked before a pixel is allocated: what
/// <see cref="RasterImageInfo"/> read from the header is only trusted as far as the codec agrees.
/// </summary>
public class RasterOverlayCacheTests
{
    private static readonly GeoBox Box = new(11.24, 48.17, 11.26, 48.18);

    [AvaloniaFact]
    public void A_real_picture_whose_header_the_codec_confirms_is_taken()
    {
        var png = Png(64, 48);

        Assert.True(RasterOverlayCache.CodecConfirms(Read(png)));
    }

    [AvaloniaFact]
    public void A_header_the_codec_reads_as_another_size_is_refused_before_decoding()
    {
        // The bytes are a 64 × 48 picture; the checked header claims 32 × 48.
        var png = Png(64, 48);
        var claimed = new RasterImage(Guid.NewGuid(), png, Box, new RasterImageInfo(RasterFormat.Png, 32, 48));

        Assert.False(RasterOverlayCache.CodecConfirms(claimed));
    }

    [AvaloniaFact]
    public void A_PNG_whose_header_was_rewritten_to_a_small_size_is_refused()
    {
        // RasterImageInfo reads the forged IHDR as 16 × 16; the codec refuses the chunk whose CRC no longer matches.
        var png = Png(64, 48);
        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(16), 16);
        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(20), 16);
        var raster = Read(png);
        Assert.Equal(16, raster.Info.Width);

        Assert.False(RasterOverlayCache.CodecConfirms(raster));
    }

    [AvaloniaFact]
    public void A_header_without_a_picture_behind_it_is_refused()
    {
        // A PNG signature and IHDR that RasterImageInfo accepts, and nothing a codec can open.
        var header = Png(64, 48)[..33];
        var raster = Read(header);

        Assert.False(RasterOverlayCache.CodecConfirms(raster));
    }

    [AvaloniaFact]
    public void A_crafted_JPEG_frame_the_codec_cannot_open_is_refused()
    {
        // A frame header of 64 × 48 with no colour components: RasterImageInfo reads its size, a
        // real decoder refuses the frame outright.
        byte[] jpeg =
        [
            0xFF, 0xD8,
            0xFF, 0xC0, 0x00, 0x08, 0x08, 0x00, 0x30, 0x00, 0x40, 0x00,
            0xFF, 0xDA, 0x00, 0x08, 0x01, 0x01, 0x00, 0x00, 0x3F, 0x00, 0xFF, 0xD9,
        ];
        var raster = Read(jpeg);
        Assert.Equal(64, raster.Info.Width);

        Assert.False(RasterOverlayCache.CodecConfirms(raster));
    }

    private static RasterImage Read(byte[] bytes)
    {
        Assert.True(RasterImage.TryRead(Guid.NewGuid(), bytes, Box, out var raster, out var error), error);
        return raster;
    }

    private static byte[] Png(int width, int height)
    {
        using var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var stream = new MemoryStream();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        return stream.ToArray();
    }
}
