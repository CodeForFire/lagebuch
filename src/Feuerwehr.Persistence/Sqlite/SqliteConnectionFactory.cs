using Microsoft.Data.Sqlite;

namespace Feuerwehr.Persistence.Sqlite;

public static class SqliteConnectionFactory
{
    public static SqliteConnection OpenReadWrite(string path)
    {
        var cn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString());
        cn.Open();
        Execute(cn, "PRAGMA journal_mode=WAL;");
        Execute(cn, "PRAGMA foreign_keys=ON;");
        return cn;
    }

    public static SqliteConnection OpenReadOnly(string path)
    {
        var cn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly
        }.ToString());
        cn.Open();
        Execute(cn, "PRAGMA foreign_keys=ON;");
        return cn;
    }

    private static void Execute(SqliteConnection cn, string sql)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
