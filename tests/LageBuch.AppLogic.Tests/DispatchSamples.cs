namespace LageBuch.AppLogic.Tests;

/// <summary>
/// Fictional FF-Agent alarm mails, shaped exactly like the real "Alarm Webhook und Mail" body: an
/// HTML page whose paragraph carries the <c>&lt;einsatz&gt;</c> XML HTML-escaped, a <c>&lt;br&gt;</c>
/// after every line, and real <c>&lt;br&gt;</c> tags inside the CDATA of <c>&lt;gefordert&gt;</c>.
/// Every value is invented (Musterstadt); a real alarm mail is Einsatzdaten and never committed.
/// </summary>
internal static class DispatchSamples
{
    public const string IncidentNumber = "T 1.2 261108 00042";

    /// <summary>The XML as FF-Agent writes it, before the mail escapes it.</summary>
    public static string Xml(
        string incidentNumber = IncidentNumber,
        string alarmedAt = "2026-11-08 17:17",
        string test = "",
        string place = "12345 Musterstadt Nord",
        string street = "Hauptstraße 12",
        string coordinates = "<wgs84 lat=\"48.1\" lng=\"11.5\"/>",
        string requested = "Florian Musterstadt Wache 2<br>Florian Musterstadt 1<br>Florian Musterstadt 2/40/1",
        string vehicles = "2/40/1") =>
        $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <einsatz version="1">
        <test>{test}</test>
        <organisation><![CDATA[ Feuerwehr Musterstadt ]]></organisation>
        <einsatzgruppe><![CDATA[ W2 ]]></einsatzgruppe>
        <kennung>
        <referenz>-/-</referenz>
        <einsatznummer>{incidentNumber}</einsatznummer>
        </kennung>
        <einsatzart>
        <stichwort><![CDATA[ THL 2 T2917 ]]></stichwort>
        <einsatztyp><![CDATA[ THL 2 VU eCall ]]></einsatztyp>
        <standardeinsatztyp><![CDATA[ Unbekannt ]]></standardeinsatztyp>
        </einsatzart>
        <meldung>
        <meldebild><![CDATA[ VU eCall - ohne Spracherwiderung ]]></meldebild>
        <einsatzhinweis><![CDATA[ 2 PKW, 3 Betroffene ]]></einsatzhinweis>
        <mitteiler><![CDATA[ Erika Musterfrau, 0171 7654321 ]]></mitteiler>
        <patient><![CDATA[ Max Mustermann ]]></patient>
        <hinweis_gesamt><![CDATA[ 2 PKW, 3 Betroffene ]]></hinweis_gesamt>
        </meldung>
        <zeiten>
        <alarmierung iso="{alarmedAt}" kurz="So, 17:17" lang="08.11.2026, 17:17"/>
        <ausruecken iso="" kurz="" lang=""/>
        </zeiten>
        <einsatzort>
        <ort><![CDATA[ {place} ]]></ort>
        <ort_kurz><![CDATA[ {place} ]]></ort_kurz>
        <strasse><![CDATA[ {street} ]]></strasse>
        <objekt><![CDATA[ ]]></objekt>
        <koordinaten>
        <gauss_krueger rechtswert="4444444.8" hochwert="5339727.4"/>
        {coordinates}
        </koordinaten>
        </einsatzort>
        <einsatzmittel>
        <gefordert><![CDATA[ {requested} ]]></gefordert>
        <fahrzeuge><![CDATA[ {vehicles} ]]></fahrzeuge>
        <massnahmen><![CDATA[ ]]></massnahmen>
        </einsatzmittel>
        </einsatz>
        """;

    /// <summary>
    /// The mail body as FF-Agent sends it: <paramref name="xml"/> HTML-escaped, a <c>&lt;br&gt;</c>
    /// at the end of each line, inside a styled page. The <c>&lt;br&gt;</c> already inside the CDATA
    /// stays a real tag, as it does in the real mail.
    /// </summary>
    public static string Html(string? xml = null)
    {
        var lines = (xml ?? Xml()).Split('\n')
            .Select(line => EscapeKeepingBreaks(line.TrimEnd('\r')) + " <br>");
        return "<html><head>\r\n<meta http-equiv=\"Content-Type\" content=\"text/html; charset=utf-8\">"
            + "<style type=\"text/css\">* {font-family:Helvetica}</style></head><body><div class=\"mail\">"
            + "<h2 class=\"blue\">Automatische Benachrichtigung - Alarm Webhook und Mail</h2>"
            + "<p>Hallo Max Mustermann,<br><br>"
            + string.Concat(lines)
            + "</p>Ihr FF-Agent Dienst<br>08.11.2026, 17:18:00</div></body></html>";
    }

    /// <summary>
    /// The mail as a saved <c>.eml</c>, the way FF-Agent sends it: one <c>text/html</c> part,
    /// base64 in 76-character lines, headers folded as Exchange folds them.
    /// </summary>
    public static string Eml(string? html = null)
    {
        var base64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(html ?? Html()));
        var lines = Enumerable.Range(0, (base64.Length + 75) / 76)
            .Select(i => base64.Substring(i * 76, Math.Min(76, base64.Length - (i * 76))));
        return "MIME-Version: 1.0\r\n"
            + "Date: Sun, 8 Nov 2026 17:18:03 +0100\r\n"
            + "From: FF-Agent <do-not-reply@ff-agent.example>\r\n"
            + "Subject:\r\n =?utf-8?Q?#FFA-LB#_|_THL_2_T2917_|_12345_Musterstadt?=\r\n"
            + "To: Max Mustermann <max.mustermann@ff-musterstadt.example>\r\n"
            + "Content-Transfer-Encoding: base64\r\n"
            + "Content-Type: text/html; charset=\"utf-8\"\r\n"
            + "\r\n"
            + string.Join("\r\n", lines) + "\r\n";
    }

    /// <summary>
    /// The mail as a mail client forwards or saves it: multipart/alternative, a plain-text part
    /// first, then the HTML part in quoted-printable with soft line breaks.
    /// </summary>
    public static string MultipartEml(string? html = null)
    {
        const string boundary = "----=_Part_42_Musterstadt";
        return "MIME-Version: 1.0\r\n"
            + "Subject: WG: #FFA-LB# | THL 2 T2917\r\n"
            + $"Content-Type: multipart/alternative;\r\n\tboundary=\"{boundary}\"\r\n"
            + "\r\n"
            + "This is a multi-part message in MIME format.\r\n"
            + $"--{boundary}\r\n"
            + "Content-Type: text/plain; charset=utf-8\r\n"
            + "\r\n"
            + "Automatische Benachrichtigung\r\n"
            + $"--{boundary}\r\n"
            + "Content-Type: text/html; charset=utf-8\r\n"
            + "Content-Transfer-Encoding: quoted-printable\r\n"
            + "\r\n"
            + QuotedPrintable(html ?? Html()) + "\r\n"
            + $"--{boundary}--\r\n";
    }

    // Encodes as a mail client does: '=' and every non-ASCII byte as =XX, lines cut at 76 with a
    // soft break "=" at the end.
    private static string QuotedPrintable(string text)
    {
        var encoded = new System.Text.StringBuilder();
        var line = 0;
        foreach (var b in System.Text.Encoding.UTF8.GetBytes(text))
        {
            var token = b is (byte)'=' or > 126 or < 32 ? $"={b:X2}" : ((char)b).ToString();
            if (line + token.Length > 75)
            {
                encoded.Append("=\r\n");
                line = 0;
            }

            encoded.Append(token);
            line += token.Length;
        }

        return encoded.ToString();
    }

    private static string EscapeKeepingBreaks(string line) =>
        string.Join("<br>", line.Split("<br>").Select(part => part
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)));
}
