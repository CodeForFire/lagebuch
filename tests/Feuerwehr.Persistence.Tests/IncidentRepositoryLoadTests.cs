using Microsoft.Data.Sqlite;

namespace Feuerwehr.Persistence.Tests;

// Load is a read path, so it must not bring a file into existence. A recent-files entry whose
// .fwincident has been moved or deleted is the everyday case: opening it used to create an empty
// database at that path and only then fail, quietly littering the incident folder.
public class IncidentRepositoryLoadTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"load-{Guid.NewGuid():N}.fwincident");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Fact]
    public void Loading_a_missing_file_reports_it_and_creates_nothing()
    {
        Assert.False(File.Exists(_path));

        var ex = Assert.Throws<FileNotFoundException>(() => new IncidentRepository().Load(_path));

        // The path rides on FileName, not in the message: the Home banner already prefixes the
        // filename, so repeating it there reads as a stutter.
        Assert.Equal(_path, ex.FileName);
        Assert.DoesNotContain(Path.GetFileName(_path), ex.Message);
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public void Loading_a_file_that_is_not_a_database_reports_it_without_truncating_it()
    {
        // Picking the wrong file in the open dialog must not damage whatever was picked.
        File.WriteAllText(_path, "nicht wirklich eine Einsatzdatei");

        Assert.ThrowsAny<Exception>(() => new IncidentRepository().Load(_path));

        Assert.Equal("nicht wirklich eine Einsatzdatei", File.ReadAllText(_path));
    }
}
