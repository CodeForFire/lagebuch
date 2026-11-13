namespace LageBuch.Domain.CoMeasurement;

/// <summary>
/// One gas a brigade measures (#410): its name, its unit, which way it gets worse and up to three
/// bands. It lives in the Stammdaten as the "Messgase" list and is copied by value into an incident
/// the first time that incident opens a Haus (see <see cref="IncidentGas"/>), so editing the
/// Stammdaten later never changes how an old Messprotokoll reads.
/// </summary>
/// <remarks>
/// Every threshold is optional. A gas without any is recorded and shown in neutral: it never earns
/// a colour nobody configured. <see cref="PlausibleUpTo"/> only ever produces a "check this" hint,
/// never a rejection.
/// <para>
/// <see cref="UpperAlarm"/> ("Anreicherung ab", #927) is the one threshold that points the other
/// way: a falling gas -- O₂ -- is also dangerous when it climbs, enrichment being a fire hazard.
/// It means nothing on a rising gas and is dropped there. It assumes no particular instrument: a
/// Gastec 31B tube reads O₂ up to 24 %, so 23 Vol-% is as much its limit as a multi-gas meter's.
/// </para>
/// </remarks>
public sealed record GasDefinition(
    string Name,
    string Unit,
    GasDirection Direction,
    decimal? Elevated,
    decimal? Dangerous,
    decimal? Lethal,
    decimal? PlausibleUpTo,
    decimal? UpperAlarm = null)
{
    /// <summary>The longest list a brigade can keep. Enforced in the editor, on import, and on an
    /// incident's copied list arriving from a file or a sync peer.</summary>
    public const int MaxCount = 8;

    /// <summary>A gas name is a tile label ("H2S", "CH4"), not a sentence.</summary>
    public const int MaxNameLength = 12;

    public const int MaxUnitLength = 10;

    /// <summary>The largest reading accepted. Far above any handheld meter's range; it is there
    /// because the value also arrives from a sync peer.</summary>
    public const decimal MaxValue = 100_000m;

    /// <summary>A reading keeps two decimals: 20,9 Vol-% O₂ needs one, %UEG sometimes two.</summary>
    public const int MaxDecimals = 2;

    /// <summary>What a Stammdaten without a Messgase list yields, and what a new Haus measures when
    /// the list is empty: CO in ppm at the alarm thresholds DGUV FBFHB-021 gives a three-threshold
    /// meter, A1 20 / A2 60 / A3 200 ppm, so the tile turns when the crew's meter alarms.</summary>
    public static GasDefinition DefaultCo { get; } = new("CO", "ppm", GasDirection.Rising, 20m, 60m, 200m, 2000m);

    /// <summary>The CO gas every Einsatz had before #410, with the bands the app used then. A file
    /// from before the gas list keeps it, so reopening an old Einsatz recolours nothing. The V43
    /// migration writes the same values in SQL.</summary>
    public static GasDefinition LegacyCo { get; } = new("CO", "ppm", GasDirection.Rising, 30m, 200m, 800m, 2000m);

    /// <summary>What the editor offers for <see cref="Unit"/>; the unit itself stays free text.</summary>
    public static IReadOnlyList<string> UnitSuggestions { get; } = ["ppm", "%UEG", "Vol-%", "mg/m³"];

    /// <summary>Whether the thresholds that are set climb in the gas's own direction:
    /// Erhöht &lt; Gefährlich &lt; Lebensgefährlich for a rising gas, the reverse for a falling
    /// one. Missing thresholds are skipped, so 30 / — / 800 is in order. A falling gas's
    /// <see cref="UpperAlarm"/> must lie above its mildest band and not beyond
    /// <see cref="PlausibleUpTo"/>; on a rising gas it is ignored, as everywhere else.</summary>
    public bool ThresholdsInOrder => BandsInOrder && UpperAlarmInOrder;

    private bool BandsInOrder
    {
        get
        {
            decimal? previous = null;
            foreach (var threshold in new[] { Elevated, Dangerous, Lethal })
            {
                if (threshold is not { } current)
                {
                    continue;
                }

                if (previous is { } p && (Direction == GasDirection.Rising ? current <= p : current >= p))
                {
                    return false;
                }

                previous = current;
            }

            return true;
        }
    }

    // The mildest falling band is the highest one set: Erhöht, else Gefährlich, else
    // Lebensgefährlich. Max rather than the first one set keeps this check independent of
    // BandsInOrder, so Sanitized can judge the two apart.
    private bool UpperAlarmInOrder =>
        Direction != GasDirection.Falling
        || UpperAlarm is not { } upper
        || (IsAboveMildestBand(upper) && IsWithinPlausibleRange(upper));

    private bool IsAboveMildestBand(decimal upper) =>
        new[] { Elevated, Dangerous, Lethal }.Max() is not { } mildest || upper > mildest;

    private bool IsWithinPlausibleRange(decimal upper) =>
        PlausibleUpTo is not { } plausible || upper <= plausible;

    /// <summary>
    /// The same gas with every rule that cannot be trusted from outside applied: name and unit
    /// trimmed and cut to their caps, an unknown direction read as rising, a negative or oversized
    /// number dropped, and thresholds that are out of order dropped altogether, so the gas falls
    /// back to neutral rather than showing a colour it has not earned. An <see cref="UpperAlarm"/>
    /// is kept only on a falling gas and only where it still fits the bands that are left; it is
    /// dropped on its own, so the falling bands keep working. Returns null for a gas
    /// without a name. Used on everything an incident file or a sync peer hands over; the
    /// Stammdaten import rejects out-of-order thresholds outright instead.
    /// </summary>
    public GasDefinition? Sanitized()
    {
        var name = Cut(Name, MaxNameLength);
        if (name.Length == 0)
        {
            return null;
        }

        var direction = Enum.IsDefined(Direction) ? Direction : GasDirection.Rising;
        var sanitized = new GasDefinition(
            name,
            Cut(Unit, MaxUnitLength),
            direction,
            InRange(Elevated),
            InRange(Dangerous),
            InRange(Lethal),
            InRange(PlausibleUpTo),
            direction == GasDirection.Falling ? InRange(UpperAlarm) : null);
        if (!sanitized.BandsInOrder)
        {
            sanitized = sanitized with { Elevated = null, Dangerous = null, Lethal = null };
        }

        return sanitized.UpperAlarmInOrder ? sanitized : sanitized with { UpperAlarm = null };
    }

    /// <summary>Caps a list at <see cref="MaxCount"/> after sanitising each entry and dropping
    /// nameless and same-named ones (first wins, case-insensitive).</summary>
    public static IReadOnlyList<GasDefinition> SanitizeList(IEnumerable<GasDefinition>? gases)
    {
        var result = new List<GasDefinition>();
        foreach (var gas in gases ?? [])
        {
            if (gas?.Sanitized() is { } clean
                && !result.Exists(g => string.Equals(g.Name, clean.Name, StringComparison.OrdinalIgnoreCase)))
            {
                result.Add(clean);
                if (result.Count == MaxCount)
                {
                    break;
                }
            }
        }

        return result;
    }

    /// <summary>Rounds a reading to <see cref="MaxDecimals"/>, or returns null when it cannot be
    /// a reading at all (negative or above <see cref="MaxValue"/>).</summary>
    public static decimal? NormalizeValue(decimal value) =>
        value is < 0 or > MaxValue ? null : decimal.Round(value, MaxDecimals, MidpointRounding.AwayFromZero);

    private static decimal? InRange(decimal? value) => value is { } v ? NormalizeValue(v) : null;

    private static string Cut(string? value, int max)
    {
        var trimmed = (value ?? string.Empty).Trim();

        // Stryker disable once Equality : cutting a string of exactly max characters to max yields the same string.
        return trimmed.Length > max ? trimmed[..max].TrimEnd() : trimmed;
    }
}
