using Microsoft.Data.Sqlite;

namespace LageBuch.Persistence.Sqlite;

/// <summary>
/// Reconciles an incident file's real columns against the schema this build expects, on every open.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Migrations"/> gates every step behind <c>version &lt; N</c>, which is only sound if
/// every build that ever stamped version N applied exactly this lineage's steps 1..N. Parallel
/// feature branches break that: a branch that numbers its own migrations 18 and 19 stamps
/// <c>schema_version = 19</c> onto a file that never received main's V18, and main then skips V18
/// forever — the file is missing a column no future migration will ever add back. That is not
/// hypothetical; it is how <c>force_units.zugfuehrer_count</c> went missing from files carrying
/// the current version marker, and every read of them failed with "no such column".
/// </para>
/// <para>
/// So the version marker is treated as an optimisation, not as proof of what is on disk. This pass
/// runs unconditionally and adds any expected column the file actually lacks. It is deliberately
/// one-directional: it never drops a table or column it does not recognise, so a file that a
/// feature branch widened keeps that branch's data and still opens on both builds.
/// </para>
/// <para>
/// <see cref="ExpectedColumns"/> covers every column of the current schema that
/// <c>ALTER TABLE ... ADD COLUMN</c> can legally add — not a primary key, and either nullable or
/// carrying a default. That is exactly the set repairable after the fact, and it has a mechanical
/// membership rule (<c>pk = 0 AND (notnull = 0 OR dflt_value IS NOT NULL)</c>) that
/// <c>SchemaReconciliationTests</c> evaluates against a freshly migrated database, so a migration
/// that adds a column without listing it here fails CI instead of a user's file.
/// </para>
/// </remarks>
internal static class SchemaGuard
{
    private static readonly string[] Tables =
    [
        "audit_events",
        "checklist_items",
        "checklist_lists",
        "co_buildings",
        "co_dwellings",
        "co_readings",
        "etb_entries",
        "etb_entry_edits",
        "force_unit_edits",
        "force_units",
        "incident_files",
        "incident_meta",
        "incident_tasks",
        "incident_timers",
        "role_assignments",
        "scba_pressure_readings",
        "scba_trupp_members",
        "scba_trupps",
    ];

    private static readonly ExpectedColumn[] Columns =
    [
        new("checklist_items", "note", "TEXT", false, null),
        new("checklist_items", "is_mandatory", "INTEGER", true, "0"),
        new("checklist_items", "kind", "INTEGER", true, "0"),
        new("checklist_items", "list_id", "TEXT", false, null),
        new("checklist_lists", "ordinal", "INTEGER", true, "0"),
        new("checklist_lists", "title", "TEXT", true, "''"),
        new("co_buildings", "floor_descriptions", "TEXT", true, "'{}'"),
        new("co_buildings", "apartment_labels", "TEXT", true, "'{}'"),
        new("co_buildings", "underground_floor_count", "INTEGER", true, "0"),
        new("co_buildings", "apartment_counts", "TEXT", true, "'{}'"),
        new("co_dwellings", "resident_name", "TEXT", false, null),
        new("co_dwellings", "key_available", "INTEGER", false, null),
        new("co_dwellings", "co_value", "INTEGER", false, null),
        new("co_readings", "value", "INTEGER", false, null),
        new("etb_entries", "from_party", "TEXT", false, null),
        new("etb_entries", "to_party", "TEXT", false, null),
        new("force_unit_edits", "previous_zugfuehrer_count", "INTEGER", true, "0"),
        new("force_units", "call_sign", "TEXT", false, null),
        new("force_units", "status", "TEXT", false, null),
        new("force_units", "notes", "TEXT", false, null),
        new("force_units", "scba_count", "INTEGER", true, "0"),
        new("force_units", "officer_count", "INTEGER", true, "0"),
        new("force_units", "zugfuehrer_count", "INTEGER", true, "0"),
        new("incident_files", "display_name", "TEXT", false, null),
        new("incident_meta", "incident_number", "TEXT", false, null),
        new("incident_meta", "ils_number", "TEXT", false, null),
        new("incident_meta", "keyword", "TEXT", false, null),
        new("incident_meta", "street", "TEXT", false, null),
        new("incident_meta", "district", "TEXT", false, null),
        new("incident_meta", "status", "TEXT", false, null),
        new("incident_meta", "closed_at", "TEXT", false, null),
        new("incident_meta", "closed_by", "TEXT", false, null),
        new("incident_tasks", "completed_at", "TEXT", false, null),
        new("incident_tasks", "completed_by", "TEXT", false, null),
        new("role_assignments", "call_sign", "TEXT", false, null),
        new("role_assignments", "from_time", "TEXT", false, null),
        new("role_assignments", "to_time", "TEXT", false, null),
        new("role_assignments", "section", "TEXT", false, null),
        new("role_assignments", "phone", "TEXT", false, null),
        new("scba_trupps", "call_sign", "TEXT", false, null),
        new("scba_trupps", "task", "TEXT", false, null),
        new("scba_trupps", "start_time", "TEXT", false, null),
        new("scba_trupps", "entry_pressure", "INTEGER", false, null),
        new("scba_trupps", "withdraw_time", "TEXT", false, null),
        new("scba_trupps", "exit_time", "TEXT", false, null),
        new("scba_trupps", "safety_trupp_id", "TEXT", false, null),
    ];

    /// <summary>Every column this build can repair, in the order it is declared above.</summary>
    public static IReadOnlyList<ExpectedColumn> ExpectedColumns => Columns;

    /// <summary>Every table of the current schema, so a newly added one cannot go unnoticed.</summary>
    public static IReadOnlyList<string> ExpectedTables => Tables;

    /// <summary>
    /// Adds every expected column the file is missing. A healthy file is left byte-identical.
    /// </summary>
    public static void Reconcile(SqliteConnection cn, SqliteTransaction tx)
    {
        ArgumentNullException.ThrowIfNull(cn);
        var present = ReadColumnsByTable(cn, tx);
        foreach (var column in Columns)
        {
            // A table that is absent entirely is out of scope -- AddColumnIfMissing would no-op on
            // it anyway, and reporting "no such table" is more honest than half-building it here.
            if (present.TryGetValue(column.Table, out var names) && !names.Contains(column.Name))
            {
                SchemaHelpers.AddColumnIfMissing(cn, tx, column.Table, column.Name, column.Ddl);
            }
        }
    }

    /// <summary>
    /// Reads the file's real shape in one sweep -- one sqlite_master query plus one
    /// pragma_table_info per table -- so the common case (nothing missing) costs no ALTER probing.
    /// </summary>
    private static Dictionary<string, HashSet<string>> ReadColumnsByTable(
        SqliteConnection cn, SqliteTransaction tx)
    {
        var tables = new List<string>();
        using (var list = cn.CreateCommand())
        {
            list.Transaction = tx;
            list.CommandText = "SELECT name FROM sqlite_master WHERE type='table';";
            using var reader = list.ExecuteReader();
            while (reader.Read())
            {
                tables.Add(reader.GetString(0));
            }
        }

        var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var table in tables)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            using var info = cn.CreateCommand();
            info.Transaction = tx;
            info.CommandText = "SELECT name FROM pragma_table_info($t);";
            info.Parameters.AddWithValue("$t", table);
            using var reader = info.ExecuteReader();
            while (reader.Read())
            {
                names.Add(reader.GetString(0));
            }

            result[table] = names;
        }

        return result;
    }
}

/// <summary>
/// One column of the current schema that <c>ALTER TABLE ... ADD COLUMN</c> can restore, kept
/// structured rather than as a DDL string so the drift test can compare type, nullability and
/// default field by field against <c>pragma_table_info</c>.
/// </summary>
/// <param name="Table">The table the column belongs to.</param>
/// <param name="Name">The column name.</param>
/// <param name="Type">The declared SQLite type, e.g. <c>TEXT</c>.</param>
/// <param name="NotNull">Whether the column is declared NOT NULL.</param>
/// <param name="Default">
/// The default as SQLite reports it in <c>pragma_table_info.dflt_value</c> — a raw literal, so a
/// string default carries its own quotes (<c>'{}'</c>). Null when the column has no default.
/// </param>
internal sealed record ExpectedColumn(
    string Table, string Name, string Type, bool NotNull, string? Default)
{
    /// <summary>The column definition as ALTER TABLE wants it.</summary>
    public string Ddl =>
        Type
        + (NotNull ? " NOT NULL" : string.Empty)
        + (Default is null ? string.Empty : $" DEFAULT {Default}");
}
