using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace LageBuch.AppLogic.Services.Dispatch;

/// <summary>
/// The text of a saved mail (<c>.eml</c>, RFC 5322 with MIME): the first <c>text/html</c> part, or
/// failing that the first <c>text/plain</c> one, decoded from base64 or quoted-printable in its
/// charset. Just enough MIME to read an alarm mail that someone saved or dropped into the alarm
/// folder; no attachments, no headers beyond the ones that say how a part is encoded.
/// <para>
/// The mail is hostile input like everything in the folder: multiparts are followed at most
/// <see cref="MaxDepth"/> levels deep and <see cref="MaxParts"/> parts in all, and every step is a
/// single forward scan.
/// </para>
/// </summary>
public static class MimeMail
{
    /// <summary>How deep nested multiparts are followed.</summary>
    public const int MaxDepth = 5;

    /// <summary>How many parts one mail may have before the rest are ignored.</summary>
    public const int MaxParts = 100;

    /// <summary>
    /// The longest header block read, for the mail and for each part. Exchange's Received chains
    /// run to tens of KB; anything past this is no alarm mail.
    /// </summary>
    public const int MaxHeaderLength = 64 * 1024;

    /// <summary>
    /// The mail's text, or false when <paramref name="content"/> is no mail (it does not open with
    /// a header block naming a Content-Type) or holds no readable text part.
    /// </summary>
    public static bool TryGetText(string content, [NotNullWhen(true)] out string? text)
    {
        ArgumentNullException.ThrowIfNull(content);
        text = null;
        if (!TrySplit(content, out var headers, out var body) || !headers.ContainsKey("content-type"))
        {
            return false;
        }

        var parts = 0;
        string? plain = null;
        text = Walk(headers, body, depth: 0, ref parts, ref plain) ?? plain;
        return text is not null;
    }

    // The first text/html leaf, depth first; the first text/plain one is kept on the side.
    private static string? Walk(Dictionary<string, string> headers, string body, int depth, ref int parts, ref string? plain)
    {
        if (depth > MaxDepth || ++parts > MaxParts)
        {
            return null;
        }

        var (type, parameters) = ContentType(headers);
        if (type.StartsWith("MULTIPART/", StringComparison.Ordinal))
        {
            if (!parameters.TryGetValue("boundary", out var boundary) || boundary.Length == 0)
            {
                return null;
            }

            foreach (var part in Parts(body, boundary))
            {
                if (TrySplit(part, out var partHeaders, out var partBody)
                    && Walk(partHeaders, partBody, depth + 1, ref parts, ref plain) is { } html)
                {
                    return html;
                }
            }

            return null;
        }

        if (type is not ("TEXT/HTML" or "TEXT/PLAIN") || Decode(headers, parameters, body) is not { } decoded)
        {
            return null;
        }

        if (type == "TEXT/HTML")
        {
            return decoded;
        }

        plain ??= decoded;
        return null;
    }

    // The parts between "--boundary" lines, up to "--boundary--". The line break before each
    // delimiter belongs to the delimiter (RFC 2046), so it is not part of the part.
    private static IEnumerable<string> Parts(string body, string boundary)
    {
        var delimiter = "--" + boundary;
        var at = body.IndexOf(delimiter, StringComparison.Ordinal);
        while (at != -1)
        {
            var lineEnd = body.IndexOf('\n', at);
            if (lineEnd == -1 || body.AsSpan(at + delimiter.Length).StartsWith("--", StringComparison.Ordinal))
            {
                yield break;
            }

            var next = body.IndexOf("\n" + delimiter, lineEnd, StringComparison.Ordinal);
            var end = next == -1 ? body.Length : next;
            var part = body[(lineEnd + 1)..end];
            yield return part.EndsWith('\r') ? part[..^1] : part;
            at = next == -1 ? -1 : next + 1;
        }
    }

    // Headers up to the first empty line, folded lines joined; false when the text does not open
    // with "Name: value" lines, which is how an HTML or XML file is told apart from a mail.
    private static bool TrySplit(string content, out Dictionary<string, string> headers, out string body)
    {
        headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        body = string.Empty;

        // Folded lines are collected per header and joined once: appending each to a string
        // copies the whole header again, which a hostile mail of continuation lines makes quadratic.
        var values = new Dictionary<string, StringBuilder>(StringComparer.OrdinalIgnoreCase);
        StringBuilder? current = null;
        var at = 0;
        while (at < content.Length && at <= MaxHeaderLength)
        {
            var lineEnd = content.IndexOf('\n', at);
            var next = lineEnd == -1 ? content.Length : lineEnd + 1;
            var line = content.AsSpan(at, (lineEnd == -1 ? content.Length : lineEnd) - at).TrimEnd('\r');
            at = next;
            if (line.IsEmpty)
            {
                body = content[at..];
                foreach (var (name, value) in values)
                {
                    headers[name] = value.ToString();
                }

                return headers.Count > 0;
            }

            if (line[0] is ' ' or '\t')
            {
                if (current is null)
                {
                    return false;
                }

                current.Append(' ').Append(line.Trim());
                continue;
            }

            var colon = line.IndexOf(':');
            if (colon <= 0 || line[..colon].IndexOfAny(" \t<") != -1)
            {
                return false;
            }

            current = new StringBuilder().Append(line[(colon + 1)..].Trim());
            values[line[..colon].Trim().ToString()] = current;
        }

        // No blank line within the cap: no header block, or one no alarm mail has.
        return false;
    }

    // "text/html; charset=\"utf-8\"" -> ("text/html", {charset: utf-8}); text/plain when absent.
    private static (string Type, Dictionary<string, string> Parameters) ContentType(Dictionary<string, string> headers)
    {
        var value = headers.GetValueOrDefault("content-type") ?? "text/plain";
        var pieces = value.Split(';');
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var piece in pieces.Skip(1))
        {
            var equals = piece.IndexOf('=', StringComparison.Ordinal);
            if (equals > 0)
            {
                parameters[piece[..equals].Trim()] = piece[(equals + 1)..].Trim().Trim('"');
            }
        }

        return (pieces[0].Trim().ToUpperInvariant(), parameters);
    }

    private static string? Decode(Dictionary<string, string> headers, Dictionary<string, string> parameters, string body)
    {
        var encoding = (headers.GetValueOrDefault("content-transfer-encoding") ?? "7bit").Trim().ToUpperInvariant();
        byte[]? bytes;
        switch (encoding)
        {
            case "BASE64":
                bytes = FromBase64(body);
                break;
            case "QUOTED-PRINTABLE":
                bytes = FromQuotedPrintable(body);
                break;
            default:
                // 7bit, 8bit or binary: the file was read as UTF-8 text already.
                return body;
        }

        return bytes is null ? null : Charset(parameters.GetValueOrDefault("charset")).GetString(bytes);
    }

    private static byte[]? FromBase64(string body)
    {
        var compact = new string(body.Where(c => !char.IsWhiteSpace(c)).ToArray());
        var bytes = new byte[compact.Length];
        return Convert.TryFromBase64String(compact, bytes, out var written) ? bytes[..written] : null;
    }

    // "=XX" is a byte, "=" at a line end a soft break; everything else stands for itself.
    private static byte[] FromQuotedPrintable(string body)
    {
        var bytes = new List<byte>(body.Length);
        for (var i = 0; i < body.Length; i++)
        {
            var c = body[i];
            if (c != '=')
            {
                bytes.Add((byte)c);
            }
            else if (i + 2 < body.Length && Uri.IsHexDigit(body[i + 1]) && Uri.IsHexDigit(body[i + 2]))
            {
                bytes.Add(Convert.ToByte(body.Substring(i + 1, 2), 16));
                i += 2;
            }
            else
            {
                // A soft line break: skip the "=" and the line end after it.
                while (i + 1 < body.Length && body[i + 1] is '\r' or '\n')
                {
                    i++;
                }
            }
        }

        return [.. bytes];
    }

    // The part's charset, Latin-1 for the Windows codepage .NET does not ship, UTF-8 otherwise.
    private static Encoding Charset(string? name)
    {
        if (string.Equals(name, "windows-1252", StringComparison.OrdinalIgnoreCase))
        {
            return Encoding.Latin1;
        }

        try
        {
            return name is null ? Encoding.UTF8 : Encoding.GetEncoding(name);
        }
        catch (ArgumentException)
        {
            // An unknown charset name: UTF-8 reads every ASCII alarm right and most of the rest.
            return Encoding.UTF8;
        }
    }
}
