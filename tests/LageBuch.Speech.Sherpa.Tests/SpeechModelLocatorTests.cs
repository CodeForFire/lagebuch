namespace LageBuch.Speech.Sherpa.Tests;

// These exist because of a bug no other test could have caught. TryLocate looked only beside the
// binary, which in a development run is bin/Debug/net10.0 -- while speech-models sits at the
// repository root. The audition harness carried its own resolver that walked *up* the tree, so the
// harness always found the voices and the app never could: every test passed, every audition
// rendered, and the running app silently fell back to its bundled clips.
//
// Nothing here needs the models themselves, only directories, so these run anywhere.
public class SpeechModelLocatorTests : IDisposable
{
    private readonly string _root = Path.Join(Path.GetTempPath(), "lagebuch-locator-" + Guid.NewGuid().ToString("N"));

    // The production layout: the models sit beside the executable and are found immediately.
    [Fact]
    public void A_models_directory_beside_the_binary_is_found()
    {
        var app = Dir("app");
        Dir("app", SpeechModelLocator.ModelsDirectoryName);

        Assert.True(SpeechModelLocator.TryLocate(out var found, out _, app));
        Assert.Equal(Path.Join(app, SpeechModelLocator.ModelsDirectoryName), found);
    }

    // The development layout, and the case that was broken: the binary is several levels below the
    // checkout root where the models live.
    [Fact]
    public void A_models_directory_above_the_binary_is_found()
    {
        var checkout = Dir("checkout");
        var models = Dir("checkout", SpeechModelLocator.ModelsDirectoryName);
        var bin = Dir("checkout", "src", "LageBuch.App", "bin", "Debug", "net10.0");

        Assert.True(SpeechModelLocator.TryLocate(out var found, out _, bin));
        Assert.Equal(models, found);
        Assert.NotEqual(checkout, found);
    }

    // The walk is bounded so a published app cannot wander into an unrelated directory far above
    // its install location.
    [Fact]
    public void A_models_directory_beyond_the_bound_is_not_found()
    {
        Dir("far", SpeechModelLocator.ModelsDirectoryName);
        var deep = Dir("far", "a", "b", "c", "d", "e", "f", "g", "h", "i", "j");

        Assert.False(SpeechModelLocator.TryLocate(out _, out var reason, deep));
        Assert.Contains("Keine Sprachmodelle", reason, StringComparison.Ordinal);
    }

    // The reason is the only signal the app has that it is about to fall back to clips, so it must
    // name where it looked rather than merely saying no.
    [Fact]
    public void A_missing_directory_explains_where_it_looked()
    {
        var empty = Dir("empty");

        Assert.False(SpeechModelLocator.TryLocate(out var found, out var reason, empty));
        Assert.Null(found);
        Assert.Contains(empty, reason, StringComparison.Ordinal);
    }

    private string Dir(params string[] parts)
    {
        var path = Path.Join([_root, .. parts]);
        Directory.CreateDirectory(path);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }
}
