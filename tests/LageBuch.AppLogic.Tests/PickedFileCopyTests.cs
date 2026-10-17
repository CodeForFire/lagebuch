using LageBuch.AppLogic.Services;
using LageBuch.Domain.Files;

namespace LageBuch.AppLogic.Tests;

public class PickedFileCopyTests
{
    [Fact]
    public void A_file_within_the_cap_is_copied_whole()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };
        using var input = new MemoryStream(bytes);
        using var output = new MemoryStream();

        PickedFileCopy.CopyCapped(input, output, maxBytes: 4);

        Assert.Equal(bytes, output.ToArray());
    }

    // #407: the size column of a content provider can lie or be missing, so the cap is counted on
    // the bytes themselves, and the copy stops instead of filling the disk first.
    [Fact]
    public void A_file_over_the_cap_is_refused_without_reading_past_it()
    {
        using var input = new EndlessStream();
        using var output = new MemoryStream();

        var ex = Assert.Throws<IOException>(() => PickedFileCopy.CopyCapped(input, output, maxBytes: 1024));

        Assert.Equal(IncidentFile.TooLargeMessage, ex.Message);
        Assert.True(output.Length <= 1024);
        Assert.True(input.Position <= 1024 + 81920);
    }

    /// <summary>A provider that never stops sending.</summary>
    private sealed class EndlessStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get; set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            Position += count;
            return count;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
