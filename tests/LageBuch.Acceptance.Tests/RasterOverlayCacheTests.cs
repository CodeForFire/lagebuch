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

    [AvaloniaFact]
    public void Hiding_and_showing_a_picture_while_it_decodes_reuses_the_decode_under_way()
    {
        using var decoder = new GatedDecoder();
        var cache = new RasterOverlayCache(decode: decoder.Decode);
        try
        {
            var photo = Full();

            cache.Use([photo]);
            cache.Use([]);
            cache.Use([photo]);
            decoder.ReleaseAll();
            Pump(() => !cache.IsBusy);

            Assert.Equal(1, decoder.Started);
            Assert.Single(cache.Use([photo]));
        }
        finally
        {
            // Releases the decoded bitmaps the test leaves behind.
            cache.Clear();
        }
    }

    [AvaloniaFact]
    public void A_new_pair_waits_until_the_pair_it_replaces_has_finished_decoding()
    {
        using var decoder = new GatedDecoder();
        var cache = new RasterOverlayCache(decode: decoder.Decode);
        try
        {
            RasterImage[] old = [Full(), Full()];
            RasterImage[] replacement = [Full(), Full()];

            cache.Use(old);
            Pump(() => decoder.Started == 2);
            cache.Use(replacement);

            // Two full-size pictures still decoding fill the budget: the new ones may not start yet.
            Assert.Equal(2, decoder.Started);
            decoder.ReleaseAll();
            Pump(() => decoder.Running == 0 && !cache.IsDecoding);
            cache.Use(replacement);
            Pump(() => decoder.Started == 4);
            decoder.ReleaseAll();
            Pump(() => !cache.IsBusy);

            Assert.Equal(2, cache.Use(replacement).Count);
        }
        finally
        {
            // Releases the decoded bitmaps the test leaves behind.
            cache.Clear();
        }
    }

    [AvaloniaFact]
    public void A_cleared_cache_still_counts_the_decodes_it_left_running()
    {
        using var decoder = new GatedDecoder();
        var cache = new RasterOverlayCache(decode: decoder.Decode);
        try
        {
            cache.Use([Full(), Full()]);
            Pump(() => decoder.Started == 2);

            cache.Clear();
            RasterImage[] next = [Full()];
            cache.Use(next);

            Assert.Equal(2, decoder.Started);
            decoder.ReleaseAll();
            Pump(() => !cache.IsDecoding);
            cache.Use(next);
            Pump(() => decoder.Started == 3);
            decoder.ReleaseAll();
            Pump(() => !cache.IsBusy);
            Assert.Single(cache.Use(next));
        }
        finally
        {
            // Releases the decoded bitmaps the test leaves behind.
            cache.Clear();
        }
    }

    [AvaloniaFact]
    public void A_picture_that_fails_to_decode_is_reported_once_and_drawn_as_nothing()
    {
        var failed = new List<Guid>();
        var cache = new RasterOverlayCache(failed: failed.Add, decode: _ => throw new InvalidOperationException("kaputt"));
        try
        {
            var photo = Full();

            cache.Use([photo]);
            Pump(() => !cache.IsBusy);

            Assert.Empty(cache.Use([photo]));
            Assert.Equal([photo.FileId], failed);
        }
        finally
        {
            // Releases the decoded bitmaps the test leaves behind.
            cache.Clear();
        }
    }

    private static RasterImage Full() =>
        new(Guid.NewGuid(), new byte[] { 1 }, Box, new RasterImageInfo(RasterFormat.Jpeg, RasterImage.MaxPixels, RasterImage.MaxPixels));

    private static void Pump(Func<bool> until) =>
        Assert.True(SpinWait.SpinUntil(
            () =>
            {
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                return until();
            },
            TimeSpan.FromSeconds(10)));

    /// <summary>Decodes to a 1 × 1 bitmap, but only once the test lets it.</summary>
    private sealed class GatedDecoder : IDisposable
    {
        private readonly SemaphoreSlim _gate = new(0);
        private int _started;
        private int _running;

        public int Started => Volatile.Read(ref _started);

        public int Running => Volatile.Read(ref _running);

        public WriteableBitmap? Decode(RasterImage raster)
        {
            Interlocked.Increment(ref _started);
            Interlocked.Increment(ref _running);
            try
            {
                _gate.Wait(TimeSpan.FromSeconds(10));
                return new WriteableBitmap(new PixelSize(1, 1), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            }
            finally
            {
                Interlocked.Decrement(ref _running);
            }
        }

        public void ReleaseAll() => _gate.Release(16);

        public void Dispose() => _gate.Dispose();
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
