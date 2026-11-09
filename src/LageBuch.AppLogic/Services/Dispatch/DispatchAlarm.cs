using System.Globalization;
using LageBuch.Domain.ValueObjects;

namespace LageBuch.AppLogic.Services.Dispatch;

/// <summary>
/// One alarm as the Leitstelle sent it, read from an alarm mail (see <see cref="FfAgentDispatchParser"/>).
/// Everything is plain values: it is copied into a new incident, never kept as a reference (#337).
/// </summary>
/// <param name="IncidentNumber">The Leitstelle's Einsatznummer, never blank.</param>
/// <param name="Keyword">The Stichwort, e.g. "THL 2 T2917".</param>
/// <param name="IncidentType">The Einsatztyp, e.g. "THL 2 VU eCall".</param>
/// <param name="Report">The Meldebild.</param>
/// <param name="Hint">The Einsatzhinweis.</param>
/// <param name="Organisation">The Feuerwehr the alarm was sent for.</param>
/// <param name="Street">The street, without its house number.</param>
/// <param name="HouseNumber">The house number, when the street ended in one.</param>
/// <param name="PostalCode">The PLZ, when the Ort began with one.</param>
/// <param name="Place">The Ort after the PLZ, Ortsteil included as the Leitstelle wrote it.</param>
/// <param name="Building">The Objekt, when the Leitstelle named one.</param>
/// <param name="Location">The WGS84 position, or null when missing or off the globe.</param>
/// <param name="AlarmedAt">When the alarm went out, or null when the mail does not say.</param>
/// <param name="IsTest">A Probealarm.</param>
/// <param name="RequestedUnits">The alarmed Einsatzmittel by Funkrufname, in the order given.</param>
public sealed record DispatchAlarm(
    string IncidentNumber,
    string? Keyword,
    string? IncidentType,
    string? Report,
    string? Hint,
    string? Organisation,
    string? Street,
    string? HouseNumber,
    string? PostalCode,
    string? Place,
    string? Building,
    GeoPoint? Location,
    DateTimeOffset? AlarmedAt,
    bool IsTest,
    IReadOnlyList<string> RequestedUnits)
{
    /// <summary>
    /// What makes this alarm the one already answered: the Einsatznummer alone would also swallow a
    /// Nachalarmierung of the same Einsatz, which is a new question to ask.
    /// </summary>
    public string Key => AlarmedAt is { } at
        ? IncidentNumber + "|" + at.ToString("O", CultureInfo.InvariantCulture)
        : IncidentNumber;
}
