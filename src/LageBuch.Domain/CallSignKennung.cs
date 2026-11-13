namespace LageBuch.Domain;

/// <summary>
/// The Fahrzeugkennung in a Funkrufname: the middle of its three numbers, the <c>12</c> in
/// <c>FFB 1/12/1</c> or <c>Florian Emmering 1/12/1</c>. It names the kind of vehicle, so the
/// Stammdaten can map it to a Fahrzeugtyp once instead of for every vehicle.
/// </summary>
public static class CallSignKennung
{
    /// <summary>
    /// The Kennung of <paramref name="callSign"/>, as written (<c>"012"</c> stays <c>"012"</c>);
    /// blanks around the slashes do not count. Null when the name does not end in three numbers
    /// split by slashes, the first standing as a word of its own: <c>Florian Musterdorf 40/1</c>
    /// has no middle number, <c>FFB 1/ELW/1</c> no number there.
    /// </summary>
    public static string? Of(string? callSign)
    {
        var parts = callSign?.Split('/');
        if (parts is not [var head, var middle, var tail])
        {
            return null;
        }

        // The first number is the last word before the first slash: "FFB 1", or "1" alone.
        var front = head.TrimEnd();
        var first = front[(LastBlank(front) + 1)..];
        var kennung = middle.Trim();
        return IsNumber(first) && IsNumber(kennung) && IsNumber(tail.Trim()) ? kennung : null;
    }

    private static int LastBlank(string text)
    {
        for (var i = text.Length - 1; i >= 0; i--)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsNumber(string text) => text.Length > 0 && text.All(char.IsAsciiDigit);
}
