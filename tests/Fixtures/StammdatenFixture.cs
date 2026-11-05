namespace LageBuch.TestFixtures;

/// <summary>
/// A fictional Stammdaten file that sets every field of every category to a value other than
/// its default, written exactly as <c>MasterDataJson.Serialize</c> writes it. Linked into each
/// test project that proves a leg of the import/export round trip loses nothing: the JSON
/// format, the store, the editor's view model and the rendered editor.
/// </summary>
internal static class StammdatenFixture
{
    public static string Path { get; } =
        System.IO.Path.Join(AppContext.BaseDirectory, "Fixtures", "stammdaten-vollstaendig.json");

    /// <summary>The fixture's text, in the form <see cref="Canonical"/> compares.</summary>
    public static string Json => Canonical(File.ReadAllText(Path));

    /// <summary>
    /// The text with line endings unified and the trailing newline dropped: the serializer
    /// writes <c>Environment.NewLine</c> and no final newline, and git may check the fixture
    /// out with CRLF on Windows. Nothing else is forgiven -- every character must match.
    /// </summary>
    public static string Canonical(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return json.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');
    }
}
