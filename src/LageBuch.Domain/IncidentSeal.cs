using System.Globalization;
using System.Text;

namespace LageBuch.Domain;

/// <summary>
/// The seal of a closed Einsatz (#598): a SHA-256 over a canonical form of the whole incident and
/// the bytes of every attachment, taken the moment it is closed. Printed in the Bericht and checked
/// again on every open, it shows whether a file and a printout still say the same thing.
/// </summary>
/// <remarks>
/// Tamper evidence, not a signature: anyone with the app and the file can edit the database and
/// seal it again. The computation lives in <c>LageBuch.AppLogic</c>, because it needs the
/// attachment bytes the domain never sees; this record only carries the result. Like every other
/// value an incident keeps, it is stored in the incident itself and never looked up elsewhere.
/// </remarks>
public sealed record IncidentSeal
{
    /// <summary>
    /// The input format this build computes. It is the first line of the hashed input and is
    /// stored with the hash, so a later format can be introduced without making an old seal
    /// unverifiable.
    /// </summary>
    public const string CurrentVersion = "lagebuch-seal-v1";

    // Crockford's base32, as HostKennung uses it: no I, L, O or U, so nothing on paper can be
    // misread as another character.
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    private const int Groups = 3;

    private const int CharsPerGroup = 4;

    public IncidentSeal(string version, string hash, IReadOnlyDictionary<Guid, string?> fileSha256)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(hash);
        ArgumentNullException.ThrowIfNull(fileSha256);
        Version = version;
        Hash = hash;
        FileSha256 = new Dictionary<Guid, string?>(fileSha256);
    }

    /// <summary>The input format the hash was computed with, e.g. <see cref="CurrentVersion"/>.</summary>
    public string Version { get; }

    /// <summary>The SHA-256 of the canonical input, as 64 lowercase hex digits.</summary>
    public string Hash { get; }

    /// <summary>
    /// The SHA-256 of each attachment's bytes at sealing time, keyed by <c>IncidentFile.Id</c>.
    /// <c>null</c> means the bytes were missing when the incident was sealed.
    /// </summary>
    public IReadOnlyDictionary<Guid, string?> FileSha256 { get; }

    /// <summary>
    /// The seal as it is stored in the incident: <c>version:hash</c>. Read back with
    /// <see cref="FromStoredForm"/>.
    /// </summary>
    public string StoredForm => $"{Version}:{Hash}";

    /// <summary>
    /// The short form printed on every page of the Bericht, e.g. <c>7K2Q-M9XD-4HPA</c>: the first
    /// 60 bits of the hash, in the same style as a sync host's Kennung. A hash that is not hex —
    /// a stored value someone edited — has no short form and reads as question marks.
    /// </summary>
    public string ShortForm
    {
        get
        {
            if (Hash.Length < 16
                || !ulong.TryParse(Hash.AsSpan(0, 16), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value))
            {
                return "????-????-????";
            }

            var shortForm = new StringBuilder((Groups * CharsPerGroup) + Groups - 1);
            for (var i = 0; i < Groups * CharsPerGroup; i++)
            {
                if (i > 0 && i % CharsPerGroup == 0)
                {
                    shortForm.Append('-');
                }

                shortForm.Append(Alphabet[(int)((value >> (59 - (5 * i))) & 31)]);
            }

            return shortForm.ToString();
        }
    }

    /// <summary>
    /// Reads a seal back from its <see cref="StoredForm"/>. Never fails: a value without a version
    /// prefix — one edited by hand — comes back with an empty <see cref="Version"/>, which the
    /// check then reports instead of refusing to open the file.
    /// </summary>
    public static IncidentSeal FromStoredForm(string stored, IReadOnlyDictionary<Guid, string?> fileSha256)
    {
        ArgumentNullException.ThrowIfNull(stored);
        var separator = stored.LastIndexOf(':');
        return separator < 0
            ? new IncidentSeal(string.Empty, stored, fileSha256)
            : new IncidentSeal(stored[..separator], stored[(separator + 1)..], fileSha256);
    }
}
