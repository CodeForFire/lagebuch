using Feuerwehr.Domain;
using Feuerwehr.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace Feuerwehr.Persistence;

public sealed class IncidentRepository
{
    private const string Iso = "O";

    public void Save(string path, Incident incident)
    {
        ArgumentNullException.ThrowIfNull(incident);
        using var cn = SqliteConnectionFactory.OpenReadWrite(path);
        Migrations.Migrate(cn);
        using var tx = cn.BeginTransaction();

        foreach (var table in new[]
                 { "incident_meta", "checklist_items", "etb_entries",
                   "role_assignments", "force_units", "audit_events" })
        {
            Exec(cn, tx, $"DELETE FROM {table};");
        }

        Run(cn, tx,
            "INSERT INTO incident_meta (id, started_at, state, incident_number, ils_number, keyword, street, district, status, closed_at, closed_by) " +
            "VALUES ($id,$started,$state,$num,$ils,$kw,$street,$district,$status,$closedAt,$closedBy);",
            p =>
            {
                p("$id", incident.Id.ToString());
                p("$started", incident.StartedAt.ToString(Iso));
                p("$state", (int)incident.State);
                p("$num", (object?)incident.IncidentNumber?.Value ?? DBNull.Value);
                p("$ils", (object?)incident.IlsNumber?.Value ?? DBNull.Value);
                p("$kw", (object?)incident.Keyword ?? DBNull.Value);
                p("$street", (object?)incident.Street ?? DBNull.Value);
                p("$district", (object?)incident.District ?? DBNull.Value);
                p("$status", (object?)incident.Status ?? DBNull.Value);
                p("$closedAt", (object?)incident.ClosedAt?.ToString(Iso) ?? DBNull.Value);
                p("$closedBy", (object?)incident.ClosedBy ?? DBNull.Value);
            });

        for (var i = 0; i < incident.Checklist.Count; i++)
        {
            var c = incident.Checklist[i];
            Run(cn, tx,
                "INSERT INTO checklist_items (id, ordinal, text, is_done, note) VALUES ($id,$o,$t,$d,$n);",
                p => { p("$id", c.Id.ToString()); p("$o", i); p("$t", c.Text); p("$d", c.IsDone ? 1 : 0); p("$n", (object?)c.Note ?? DBNull.Value); });
        }

        for (var i = 0; i < incident.Journal.Count; i++)
        {
            var e = incident.Journal[i];
            Run(cn, tx,
                "INSERT INTO etb_entries (id, ordinal, timestamp, direction, from_party, to_party, text, entered_by) VALUES ($id,$o,$ts,$dir,$from,$to,$txt,$by);",
                p =>
                {
                    p("$id", e.Id.ToString()); p("$o", i); p("$ts", e.Timestamp.ToString(Iso));
                    p("$dir", (int)e.Direction); p("$from", (object?)e.From ?? DBNull.Value);
                    p("$to", (object?)e.To ?? DBNull.Value); p("$txt", e.Text); p("$by", e.EnteredBy);
                });
        }

        for (var i = 0; i < incident.Roles.Count; i++)
        {
            var r = incident.Roles[i];
            Run(cn, tx,
                "INSERT INTO role_assignments (id, ordinal, role, person_name, call_sign, from_time, to_time) VALUES ($id,$o,$role,$name,$cs,$from,$to);",
                p =>
                {
                    p("$id", r.Id.ToString()); p("$o", i); p("$role", r.Role); p("$name", r.PersonName);
                    p("$cs", (object?)r.CallSign ?? DBNull.Value);
                    p("$from", (object?)r.From?.ToString(Iso) ?? DBNull.Value);
                    p("$to", (object?)r.To?.ToString(Iso) ?? DBNull.Value);
                });
        }

        for (var i = 0; i < incident.Forces.Count; i++)
        {
            var f = incident.Forces[i];
            Run(cn, tx,
                "INSERT INTO force_units (id, ordinal, brigade, call_sign, personnel_count, status, notes) VALUES ($id,$o,$b,$cs,$pc,$st,$n);",
                p =>
                {
                    p("$id", f.Id.ToString()); p("$o", i); p("$b", f.Brigade);
                    p("$cs", (object?)f.CallSign ?? DBNull.Value); p("$pc", f.PersonnelCount);
                    p("$st", (object?)f.Status ?? DBNull.Value); p("$n", (object?)f.Notes ?? DBNull.Value);
                });
        }

        for (var i = 0; i < incident.Audit.Count; i++)
        {
            var a = incident.Audit[i];
            Run(cn, tx,
                "INSERT INTO audit_events (ordinal, at, action, by_operator) VALUES ($o,$at,$act,$by);",
                p => { p("$o", i); p("$at", a.At.ToString(Iso)); p("$act", a.Action); p("$by", a.By); });
        }

        tx.Commit();
    }

    private static void Run(SqliteConnection cn, SqliteTransaction tx, string sql, Action<Action<string, object>> bind)
    {
        using var cmd = cn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        bind((name, value) => cmd.Parameters.AddWithValue(name, value));
        cmd.ExecuteNonQuery();
    }

    private static void Exec(SqliteConnection cn, SqliteTransaction tx, string sql)
    {
        using var cmd = cn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
