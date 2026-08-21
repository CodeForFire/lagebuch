using Microsoft.Data.Sqlite;

namespace LageBuch.Persistence.Sqlite;

/// <summary>
/// Idempotent schema-widening helpers shared by both SQLite stores — the per-incident file (via
/// <see cref="Migrations"/>, inside its versioned transaction) and the master-data file (which has
/// no version marker and re-checks on every open, so the transaction argument may be null there).
/// SQLite has no ALTER TABLE ADD COLUMN IF NOT EXISTS, and a duplicate ADD is a hard error, so every
/// caller needs the same existence checks first.
/// </summary>
internal static class SchemaHelpers
{
    public static void AddColumnIfMissing(
        SqliteConnection cn, SqliteTransaction? tx, string table, string column, string type)
    {
        if (!TableExists(cn, tx, table) || ColumnExists(cn, tx, table, column))
            return;
        using var cmd = cn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {type};";
        cmd.ExecuteNonQuery();
    }

    public static bool TableExists(SqliteConnection cn, SqliteTransaction? tx, string table)
    {
        using var cmd = cn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name=$t;";
        cmd.Parameters.AddWithValue("$t", table);
        return (long)cmd.ExecuteScalar()! > 0;
    }

    public static bool ColumnExists(SqliteConnection cn, SqliteTransaction? tx, string table, string column)
    {
        using var cmd = cn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"SELECT count(*) FROM pragma_table_info('{table}') WHERE name=$c;";
        cmd.Parameters.AddWithValue("$c", column);
        return (long)cmd.ExecuteScalar()! > 0;
    }
}
