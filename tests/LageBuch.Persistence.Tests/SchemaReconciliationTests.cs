using LageBuch.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace LageBuch.Persistence.Tests;

/// <summary>
/// Covers <see cref="SchemaGuard"/>: a file whose schema_version marker over-promises -- because a
/// build from a parallel branch stamped a number of its own -- must still open, with the columns it
/// never received added back.
/// </summary>
public class SchemaReconciliationTests : IDisposable
{
    private readonly string _path = Path.Join(Path.GetTempPath(), $"guard-{Guid.NewGuid():N}.fwincident");

    public static TheoryData<string, string> RepairableColumns()
    {
        var data = new TheoryData<string, string>();
        foreach (var column in SchemaGuard.ExpectedColumns)
        {
            data.Add(column.Table, column.Name);
        }

        return data;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Stamped_current_but_missing_an_additive_column_is_repaired_on_load()
    {
        // The file reported in the wild: schema_version says 21, but V18's two columns were never
        // applied because a branch build had already pushed the marker past 18. Every `version < N`
        // is false, so nothing re-adds them and Load dies on "no such column".
        SaveIncidentWithAStrengthCorrection();
        ExecuteRaw(
            "ALTER TABLE force_units DROP COLUMN zugfuehrer_count;" +
            "ALTER TABLE force_unit_edits DROP COLUMN previous_zugfuehrer_count;");

        var loaded = IncidentRepository.Load(_path);

        var force = Assert.Single(loaded.Forces);
        var edit = Assert.Single(force.Edits);

        // The repair restores the column, not the lost value: both read back as the declared
        // default. Asserting that explicitly keeps the promise honest.
        Assert.Equal(0, force.ZugfuehrerCount);
        Assert.Equal(0, edit.PreviousZugfuehrerCount);
        Assert.Equal(12, edit.PreviousPersonnelCount);
        Assert.True(ColumnExists("force_units", "zugfuehrer_count"));
        Assert.True(ColumnExists("force_unit_edits", "previous_zugfuehrer_count"));
        Assert.Equal(Migrations.CurrentVersion, ReadVersion());
    }

    [Fact]
    public void A_foreign_branch_lineage_opens_and_keeps_its_own_tables()
    {
        // The full shape of the incident: the branch build both widened the file with a table of
        // its own and left the marker at its own CurrentVersion. Reconciliation is one-directional
        // -- it adds what this build needs and drops nothing it does not recognise, so the branch
        // still finds its data after the file has been opened by main.
        SaveIncidentWithAStrengthCorrection();
        ExecuteRaw(
            "ALTER TABLE force_units DROP COLUMN zugfuehrer_count;" +
            "ALTER TABLE force_unit_edits DROP COLUMN previous_zugfuehrer_count;" +
            "CREATE TABLE wass_leitungen (id TEXT PRIMARY KEY, laenge INTEGER NOT NULL);" +
            "INSERT INTO wass_leitungen (id, laenge) VALUES ('a', 120);" +
            "DELETE FROM schema_version; INSERT INTO schema_version (version) VALUES (19);");

        var loaded = IncidentRepository.Load(_path);

        Assert.Single(loaded.Forces);
        Assert.True(ColumnExists("force_units", "zugfuehrer_count"));

        using var cn = SqliteConnectionFactory.OpenReadOnly(_path);
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT laenge FROM wass_leitungen WHERE id = 'a';";
        Assert.Equal(120L, Convert.ToInt64(cmd.ExecuteScalar()!, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Theory]
    [MemberData(nameof(RepairableColumns))]
    public void Every_repairable_column_is_restored_after_being_dropped(string table, string column)
    {
        // Exhaustive rather than illustrative: the next migration's column is covered the moment it
        // is declared. Safe because the declared set excludes primary keys, and the schema has no
        // indexes, triggers or views for DROP COLUMN to trip over.
        SaveIncidentWithAStrengthCorrection();
        ExecuteRaw($"ALTER TABLE {table} DROP COLUMN {column};");
        Assert.False(ColumnExists(table, column));

        using (var cn = SqliteConnectionFactory.OpenReadWrite(_path))
        {
            Migrations.Migrate(cn);
        }

        SqliteConnection.ClearAllPools();
        Assert.True(ColumnExists(table, column));
    }

    [Fact]
    public void Declared_schema_matches_a_freshly_migrated_database()
    {
        // The anti-drift guard. A migration that adds a column without listing it in SchemaGuard
        // reintroduces exactly this bug for every file that skips that migration -- so it fails
        // here, in CI, instead of in someone's Einsatz.
        using (var cn = SqliteConnectionFactory.OpenReadWrite(_path))
        {
            Migrations.Migrate(cn);
        }

        SqliteConnection.ClearAllPools();

        var (actualTables, actualColumns) = ReadRepairableSchema();

        Assert.Equal(
            SchemaGuard.ExpectedTables.OrderBy(t => t, StringComparer.Ordinal),
            actualTables.OrderBy(t => t, StringComparer.Ordinal));

        var declared = SchemaGuard.ExpectedColumns.ToHashSet();
        var missing = actualColumns.Where(c => !declared.Contains(c)).ToList();
        var stale = declared.Where(c => !actualColumns.Contains(c)).ToList();

        var missingMessage = "Not declared in SchemaGuard.ExpectedColumns, so a file that skipped "
            + $"the migration adding them can never be repaired: {Describe(missing)}. Add them there.";
        var staleMessage = "Declared in SchemaGuard.ExpectedColumns but not in a freshly migrated "
            + $"database (gone, renamed, or a changed type/default): {Describe(stale)}. Update it there.";
        Assert.True(missing.Count == 0, missingMessage);
        Assert.True(stale.Count == 0, staleMessage);
    }

    [Fact]
    public void Reconciliation_does_not_modify_a_healthy_file()
    {
        SaveIncidentWithAStrengthCorrection();
        var before = ReadSchemaSql();

        using (var cn = SqliteConnectionFactory.OpenReadWrite(_path))
        {
            Migrations.Migrate(cn);
        }

        SqliteConnection.ClearAllPools();
        Assert.Equal(before, ReadSchemaSql());
    }

    private static string Describe(IEnumerable<ExpectedColumn> columns) =>
        string.Join(", ", columns.Select(c => $"{c.Table}.{c.Name} ({c.Ddl})"));

    /// <summary>
    /// A saved incident carrying both a force unit and a strength correction, so force_units and
    /// force_unit_edits each hold a row whose columns the damage below can be applied to.
    /// </summary>
    private void SaveIncidentWithAStrengthCorrection()
    {
        var clock = new Clock();
        var op = new Domain.SessionOperator("Müller", "FFB 12/1");
        var incident = Domain.Incident.Start(clock, op, "Brand");
        incident.AddForceUnit(clock, op, "FFB", 12, callSign: "FFB 1/40/1", scbaCount: 6, officerCount: 1, zugfuehrerCount: 2);
        clock.Now = clock.Now.AddMinutes(3);
        incident.UpdateForceStrength(
            clock, op, incident.Forces[0].Id, officerCount: 1, personnelCount: 14, scbaCount: 7, zugfuehrerCount: 3);

        IncidentRepository.Save(_path, incident);
        SqliteConnection.ClearAllPools();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Security",
        "CA2100",
        Justification = "Audited: test-only DDL built from compile-time constants and SchemaGuard's own declared identifiers; no external input.")]
    private void ExecuteRaw(string sql)
    {
        using (var cn = SqliteConnectionFactory.OpenReadWrite(_path))
        using (var cmd = cn.CreateCommand())
        {
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();
    }

    private bool ColumnExists(string table, string column)
    {
        using var cn = SqliteConnectionFactory.OpenReadOnly(_path);
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM pragma_table_info($t) WHERE name = $c;";
        cmd.Parameters.AddWithValue("$t", table);
        cmd.Parameters.AddWithValue("$c", column);
        return (long)cmd.ExecuteScalar()! > 0;
    }

    private int ReadVersion()
    {
        using var cn = SqliteConnectionFactory.OpenReadOnly(_path);
        return Migrations.GetVersion(cn);
    }

    private string ReadSchemaSql()
    {
        using var cn = SqliteConnectionFactory.OpenReadOnly(_path);
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT group_concat(sql, ';') FROM (SELECT sql FROM sqlite_master ORDER BY name);";
        return cmd.ExecuteScalar()?.ToString() ?? string.Empty;
    }

    /// <summary>
    /// The file's tables, and the columns of them that ALTER TABLE could add back -- the same
    /// membership rule SchemaGuard's list is built on, evaluated against the real database.
    /// </summary>
    private (List<string> Tables, HashSet<ExpectedColumn> Columns) ReadRepairableSchema()
    {
        using var cn = SqliteConnectionFactory.OpenReadOnly(_path);

        var tables = new List<string>();
        using (var list = cn.CreateCommand())
        {
            list.CommandText =
                "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' " +
                "AND name <> 'schema_version';";
            using var reader = list.ExecuteReader();
            while (reader.Read())
            {
                tables.Add(reader.GetString(0));
            }
        }

        var columns = new HashSet<ExpectedColumn>();
        foreach (var table in tables)
        {
            using var info = cn.CreateCommand();
            info.CommandText =
                "SELECT name, type, \"notnull\", dflt_value FROM pragma_table_info($t) " +
                "WHERE pk = 0 AND (\"notnull\" = 0 OR dflt_value IS NOT NULL);";
            info.Parameters.AddWithValue("$t", table);
            using var reader = info.ExecuteReader();
            while (reader.Read())
            {
                columns.Add(new ExpectedColumn(
                    table,
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetInt64(2) == 1,
                    reader.IsDBNull(3) ? null : reader.GetString(3)));
            }
        }

        return (tables, columns);
    }

    private sealed class Clock : Domain.Time.IClock
    {
        public DateTimeOffset Now { get; set; } = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));
    }
}
