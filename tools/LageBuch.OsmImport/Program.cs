using System.Globalization;
using System.Text.Json;
using LageBuch.Osm;

namespace LageBuch.OsmImport;

/// <summary>
/// Fetches a Gemeinde's Straßen and Ortsteile from OpenStreetMap and writes them in the Stammdaten
/// file format (#461): either as a fragment holding just those two keys, or merged into an
/// exported Stammdaten file in place, ready for IMPORTIEREN.
/// </summary>
internal static class Program
{
    private const string Usage = """
        Aufruf: LageBuch.OsmImport (--ags <Gemeindeschlüssel> | --relation <OSM-Id>)
                                   [--out <Datei> | --merge <Stammdaten.json>] [--endpoint <URL>]

          --ags       amtlicher Gemeindeschlüssel, acht Ziffern (z. B. 09179121)
          --relation  OSM-Id der Gemeindegrenze, für Gemeinden ohne Schlüssel
          --out       districts und streets als JSON-Fragment in diese Datei (sonst stdout)
          --merge     Ortsteile und Straßen in dieser exportierten Stammdaten-Datei ersetzen
          --endpoint  anderer Overpass-Server (Standard: overpass-api.de)

        Daten © OpenStreetMap-Mitwirkende, ODbL 1.0 -- https://www.openstreetmap.org/copyright
        """;

    private static async Task<int> Main(string[] args)
    {
        if (!Options.TryParse(args, out var options, out var error))
        {
            await Console.Error.WriteLineAsync(error);
            await Console.Error.WriteLineAsync(Usage);
            return 2;
        }

        StreetList list;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(4) };
            var client = new OverpassClient(http, options.Endpoint);
            list = StreetExtractor.Extract(await client.QueryAsync(options.Query));
        }
        catch (OverpassException e)
        {
            await Console.Error.WriteLineAsync(e.Message);
            return 1;
        }

        try
        {
            await WriteAsync(options, list);
        }
        catch (IOException e)
        {
            await Console.Error.WriteLineAsync(e.Message);
            return 1;
        }
        catch (UnauthorizedAccessException e)
        {
            await Console.Error.WriteLineAsync(e.Message);
            return 1;
        }
        catch (JsonException e)
        {
            await Console.Error.WriteLineAsync($"{options.MergePath} ist keine Stammdaten-Datei: {e.Message}");
            return 1;
        }

        await Console.Error.WriteLineAsync(string.Create(
            CultureInfo.InvariantCulture,
            $"{list.Streets.Count} Straßen, {list.Districts.Count} Ortsteile. Daten © OpenStreetMap-Mitwirkende, ODbL 1.0."));
        return 0;
    }

    private static async Task WriteAsync(Options options, StreetList list)
    {
        if (options.MergePath is { } merge)
        {
            await File.WriteAllTextAsync(merge, list.MergeInto(await File.ReadAllTextAsync(merge)));
        }
        else if (options.OutPath is { } output)
        {
            await File.WriteAllTextAsync(output, list.ToJsonFragment());
        }
        else
        {
            await Console.Out.WriteAsync(list.ToJsonFragment());
        }
    }

    private sealed record Options(string Query, Uri? Endpoint, string? OutPath, string? MergePath)
    {
        public static bool TryParse(
            string[] args,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Options? options,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out string? error)
        {
            options = null;
            string? query = null;
            Uri? endpoint = null;
            string? outPath = null;
            string? mergePath = null;

            for (var i = 0; i < args.Length; i++)
            {
                if (i + 1 >= args.Length)
                {
                    error = $"{args[i]}: Wert fehlt.";
                    return false;
                }

                var value = args[++i];
                try
                {
                    switch (args[i - 1])
                    {
                        case "--ags":
                            query = OverpassQuery.ForMunicipalityKey(value);
                            break;
                        case "--relation" when long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id):
                            query = OverpassQuery.ForRelation(id);
                            break;
                        case "--endpoint" when Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps:
                            endpoint = uri;
                            break;
                        case "--out":
                            outPath = Path.GetFullPath(value);
                            break;
                        case "--merge":
                            mergePath = Path.GetFullPath(value);
                            break;
                        default:
                            error = $"{args[i - 1]} {value}: unbekannt oder ungültig.";
                            return false;
                    }
                }
                catch (ArgumentException e)
                {
                    error = e.Message;
                    return false;
                }
            }

            if (query is null)
            {
                error = "--ags oder --relation fehlt.";
                return false;
            }

            if (outPath is not null && mergePath is not null)
            {
                error = "--out und --merge schließen einander aus.";
                return false;
            }

            options = new Options(query, endpoint, outPath, mergePath);
            error = null;
            return true;
        }
    }
}
