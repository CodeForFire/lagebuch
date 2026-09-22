namespace LageBuch.Speech.Sherpa.Tests;

/// <summary>
/// The corpus the voice audition is judged on.
/// </summary>
/// <remarks>
/// Every line is real: taken from <c>docs/samples/uebung.fwincident</c>, from the auto-generated
/// sentences in <c>LageBuch.Domain/Incident.cs</c>, or from <c>ScbaViewModel</c>'s announcements.
/// Nothing here is invented, because a voice that handles made-up prose and then stumbles over
/// "Stärke 0/1/8/9" has not been tested.
/// </remarks>
internal static class AuditionTexts
{
    public static IReadOnlyList<AuditionText> All { get; } =
    [
        new()
        {
            Key = "cue-ils",
            Text = "Rückmeldung an ILS fällig",
            Probes = "the existing cue; ILS",
        },
        new()
        {
            Key = "druckabfrage",
            Text = "Druckabfrage Florian Musterstadt 40/1, Trupp 1 Angriffstrupp",
            Probes = "call-sign digits, Trupp",
        },
        new()
        {
            Key = "rueckzugsalarm",
            Text = "Rückzugsalarm Florian Musterstadt 40/1 · Trupp 1 (Angriffstrupp): "
                + "Rückzugsdruck erreicht (45 bar)",
            Probes = "the real alarm, end to end",
        },
        new()
        {
            Key = "bereitgestellt",
            Text = "Trupp 1 (CSA-Trupp) bereitgestellt: Müller / Schmidt / Huber, "
                + "Einstiegsdruck 300 bar",
            Probes = "CSA, names, pressure",
        },
        new()
        {
            Key = "lpa",
            Text = "LPA-Trupp, 60 Minuten, Einstiegsdruck 300 bar",
            Probes = "LPA",
        },
        new()
        {
            Key = "einheit",
            Text = "Einheit aufgenommen: FF Musterstadt (Florian Musterstadt 40/1), "
                + "Stärke 0/1/8/9, davon 4 AGT — Status: Im Einsatz",
            Probes = "Stärke slashes vs call-sign slashes, AGT, em-dash",
        },
        new()
        {
            Key = "alarmierung",
            Text = "Alarmierung B 3 Zimmerbrand, Hauptstraße 12, Rauch aus dem 2. OG, "
                + "Personen vermutlich noch im Gebäude",
            Probes = "real ETB entry; B 3, 2. OG, street name",
        },
        new()
        {
            Key = "lagemeldung",
            Text = "Lagemeldung: Zimmerbrand im 2. OG, Menschenrettung über DLK eingeleitet, "
                + "Innenangriff mit 1 Trupp",
            Probes = "DLK, long sentence",
        },
        new()
        {
            Key = "co-messung",
            Text = "CO-Messung Hauptstraße 12, 2. OG, Whg. 1: 120 ppm",
            Probes = "Whg., ppm, CO",
        },
        new()
        {
            Key = "co-protokoll",
            Text = "CO-Messprotokoll eröffnet: Hauptstraße 12 (EG–2. OG, 2 Wohnungen je Geschoss)",
            Probes = "en-dash floor range",
        },
        new()
        {
            Key = "etb-zeile",
            Text = "09:17 Ausgang Florian Musterstadt 11/1 an ILS",
            Probes = "HH:mm, Richtung labels",
        },
        new()
        {
            Key = "einsatznummer",
            Text = "Einsatznummer B 1.2 260622 0042",
            Probes = "the Bavarian number, the nastiest case",
        },
        new()
        {
            Key = "uebergabe",
            Text = "Funktion EL übergeben: Müller → Schmidt",
            Probes = "EL, the arrow",
        },
        new()
        {
            Key = "unter-pa",
            Text = "ACHTUNG: 2 Atemschutztrupp(s) noch unter PA.",
            Probes = "PA, the parenthetical plural",
        },
        new()
        {
            Key = "nachforderung",
            Text = "Nachforderung: 1 LF zur Ablösung, RD zur Absicherung",
            Probes = "LF, RD",
        },
    ];

    /// <summary>
    /// The lines rendered <em>unnormalized</em> as well, on the reference voices only. Hearing the
    /// raw form on every voice would treble the page for no extra information: how a voice sounds
    /// and what the normalizer did are separate questions.
    /// </summary>
    public static IReadOnlyList<string> RawComparisonKeys { get; } =
        ["rueckzugsalarm", "einheit", "co-messung", "etb-zeile", "einsatznummer", "unter-pa"];
}

/// <summary>One line the audition speaks, and what it is there to expose.</summary>
internal sealed record AuditionText
{
    public required string Key { get; init; }

    public required string Text { get; init; }

    public required string Probes { get; init; }
}
