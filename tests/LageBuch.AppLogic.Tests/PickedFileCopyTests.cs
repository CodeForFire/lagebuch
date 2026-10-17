using LageBuch.AppLogic.Services;

namespace LageBuch.AppLogic.Tests;

public class PickedFileCopyTests
{
    private const string TooLarge = "zu groß";

    [Fact]
    public void A_file_exactly_at_the_cap_is_copied_whole()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };
        using var input = new MemoryStream(bytes);
        using var output = new MemoryStream();

        PickedFileCopy.CopyCapped(input, output, maxBytes: 4, TooLarge);

        Assert.Equal(bytes, output.ToArray());
    }

    // The boundary has to match FilesViewModel's `sizeBytes > IncidentFile.MaxSizeBytes`: one byte
    // over is refused, exactly at the cap is not.
    [Fact]
    public void A_file_one_byte_over_the_cap_is_refused_with_the_given_message()
    {
        using var input = new MemoryStream(new byte[] { 1, 2, 3, 4, 5 });
        using var output = new MemoryStream();

        var ex = Assert.Throws<IOException>(() => PickedFileCopy.CopyCapped(input, output, maxBytes: 4, TooLarge));

        Assert.Equal(TooLarge, ex.Message);
    }

    // #407: the size column of a content provider can lie or be missing, so the cap is counted on
    // the bytes themselves. A stream that never ends only returns from this call because the copy
    // stops at the cap, and nothing past the cap reaches the output.
    [Fact]
    public void An_endless_file_is_refused_and_nothing_past_the_cap_is_written()
    {
        using var input = new EndlessStream();
        using var output = new MemoryStream();

        Assert.Throws<IOException>(() => PickedFileCopy.CopyCapped(input, output, maxBytes: 1024, TooLarge));

        Assert.True(output.Length <= 1024);
    }

    /// <summary>A provider that never stops sending.</summary>
    private sealed class EndlessStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => count;

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
