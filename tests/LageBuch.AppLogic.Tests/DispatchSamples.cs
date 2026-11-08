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

    private static string EscapeKeepingBreaks(string line) =>
        string.Join("<br>", line.Split("<br>").Select(part => part
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)));
}
