namespace LageBuch.AppLogic.Services;

/// <summary>
/// Copies a picked file whose size is not known up front: Android hands the app a
/// <c>content://</c> stream, and the provider's size column can be missing or wrong (#407).
/// </summary>
public static class PickedFileCopy
{
    private const int BufferSize = 81920;

    /// <summary>
    /// Copies <paramref name="input"/> to <paramref name="output"/>, stopping as soon as more than
    /// <paramref name="maxBytes"/> have arrived rather than after the whole file is on disk.
    /// </summary>
    /// <exception cref="IOException">
    /// The input is larger than <paramref name="maxBytes"/>; the message is
    /// <paramref name="tooLargeMessage"/>, so the user reads the same wording the desktop's own size
    /// check for that kind of file shows. What was already written stays in
    /// <paramref name="output"/> for the caller to discard.
    /// </exception>
    public static void CopyCapped(Stream input, Stream output, long maxBytes, string tooLargeMessage)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(tooLargeMessage);
        var buffer = new byte[BufferSize];
        long total = 0;
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (total > maxBytes)
            {
                throw new IOException(tooLargeMessage);
            }

            output.Write(buffer, 0, read);
        }
    }
}
