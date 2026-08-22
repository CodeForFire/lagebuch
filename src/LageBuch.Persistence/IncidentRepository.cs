using LageBuch.Domain;
using LageBuch.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace LageBuch.Persistence;

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
                 { "incident_meta", "checklist_items", "etb_entries", "etb_entry_edits",
                   "role_assignments", "force_units", "scba_trupps",
                   "scba_trupp_members", "scba_pressure_readings", "audit_events",
                   "incident_timers", "incident_files" })
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
                // ils_number is retired: the complete Einsatznummer lives in incident_number now.
                // The column is kept (dormant) so the schema is unchanged; always written null.
                p("$ils", DBNull.Value);
                p("$kw", (object?)incident.Keyword ?? DBNull.Value);
                p("$street", (object?)incident.Street ?? DBNull.Value);
                p("$district", (object?)incident.District ?? DBNull.Value);
                p("$status", (object?)incident.Status ?? DBNull.Value);
                p("$closedAt", (object?)incident.ClosedAt?.ToString(Iso) ?? DBNull.Value);
                p("$closedBy", (object?)incident.ClosedBy ?? DBNull.Value);
            });

        WriteChecklist(cn, tx, incident.ChecklistAufbau, kind: 0);
        WriteChecklist(cn, tx, incident.ChecklistAbbau, kind: 1);

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

            for (var j = 0; j < e.Edits.Count; j++)
            {
                var edit = e.Edits[j];
                Run(cn, tx,
                    "INSERT INTO etb_entry_edits (id, entry_id, ordinal, previous_text, edited_by, edited_at) VALUES ($id,$eid,$o,$txt,$by,$at);",
                    p =>
                    {
                        p("$id", Guid.NewGuid().ToString()); p("$eid", e.Id.ToString()); p("$o", j);
                        p("$txt", edit.PreviousText); p("$by", edit.EditedBy); p("$at", edit.EditedAt.ToString(Iso));
                    });
            }
        }

        for (var i = 0; i < incident.Roles.Count; i++)
        {
            var r = incident.Roles[i];
            Run(cn, tx,
                "INSERT INTO role_assignments (id, ordinal, role, person_name, call_sign, from_time, to_time, section, phone) VALUES ($id,$o,$role,$name,$cs,$from,$to,$sec,$ph);",
                p =>
                {
                    p("$id", r.Id.ToString()); p("$o", i); p("$role", r.Role); p("$name", r.PersonName);
                    p("$cs", (object?)r.CallSign ?? DBNull.Value);
                    p("$from", (object?)r.From?.ToString(Iso) ?? DBNull.Value);
                    p("$to", (object?)r.To?.ToString(Iso) ?? DBNull.Value);
                    p("$sec", (object?)r.Section ?? DBNull.Value);
                    p("$ph", (object?)r.Phone ?? DBNull.Value);
                });
        }

        for (var i = 0; i < incident.Forces.Count; i++)
        {
            var f = incident.Forces[i];
            Run(cn, tx,
                "INSERT INTO force_units (id, ordinal, brigade, call_sign, personnel_count, scba_count, status, notes) VALUES ($id,$o,$b,$cs,$pc,$ac,$st,$n);",
                p =>
                {
                    p("$id", f.Id.ToString()); p("$o", i); p("$b", f.Brigade);
                    p("$cs", (object?)f.CallSign ?? DBNull.Value); p("$pc", f.PersonnelCount);
                    p("$ac", f.ScbaCount);
                    p("$st", (object?)f.Status ?? DBNull.Value); p("$n", (object?)f.Notes ?? DBNull.Value);
                });
        }

        for (var i = 0; i < incident.ScbaTrupps.Count; i++)
        {
            var t = incident.ScbaTrupps[i];
            Run(cn, tx,
                "INSERT INTO scba_trupps (id, ordinal, designation, call_sign, task, registered_at, start_time, start_pressure, max_duration_minutes, return_pressure_bar, pressure_control_interval_minutes, exit_time) " +
                "VALUES ($id,$o,$des,$cs,$task,$reg,$start,$sp,$max,$ret,$interval,$exit);",
                p =>
                {
                    p("$id", t.Id.ToString()); p("$o", i); p("$des", t.Designation);
                    p("$cs", (object?)t.CallSign ?? DBNull.Value); p("$task", (object?)t.Task ?? DBNull.Value);
                    p("$reg", t.RegisteredAt.ToString(Iso));
                    p("$start", (object?)t.StartTime?.ToString(Iso) ?? DBNull.Value);
                    p("$sp", (object?)t.StartPressure ?? DBNull.Value);
                    p("$max", t.MaxDurationMinutes); p("$ret", t.ReturnPressureBar);
                    p("$interval", t.PressureControlIntervalMinutes);
                    p("$exit", (object?)t.ExitTime?.ToString(Iso) ?? DBNull.Value);
                });

            for (var j = 0; j < t.Members.Count; j++)
            {
                var member = t.Members[j];
                Run(cn, tx,
                    "INSERT INTO scba_trupp_members (trupp_id, ordinal, role, name) VALUES ($tid,$o,$role,$name);",
                    p =>
                    {
                        p("$tid", t.Id.ToString()); p("$o", j);
                        p("$role", (int)member.Role); p("$name", member.Name);
                    });
            }

            for (var j = 0; j < t.PressureReadings.Count; j++)
            {
                var reading = t.PressureReadings[j];
                Run(cn, tx,
                    "INSERT INTO scba_pressure_readings (id, trupp_id, ordinal, reading_time, bar) VALUES ($id,$tid,$o,$time,$bar);",
                    p =>
                    {
                        p("$id", Guid.NewGuid().ToString()); p("$tid", t.Id.ToString()); p("$o", j);
                        p("$time", reading.Time.ToString(Iso)); p("$bar", reading.Bar);
                    });
            }
        }

        for (var i = 0; i < incident.Audit.Count; i++)
        {
            var a = incident.Audit[i];
            Run(cn, tx,
                "INSERT INTO audit_events (ordinal, at, action, by_operator) VALUES ($o,$at,$act,$by);",
                p => { p("$o", i); p("$at", a.At.ToString(Iso)); p("$act", a.Action); p("$by", a.By); });
        }

        foreach (var t in incident.Timers)
        {
            Run(cn, tx,
                "INSERT INTO incident_timers (key, cycle_anchor, interval_minutes, recurring_interval_minutes, is_running) VALUES ($k,$a,$i,$r,$run);",
                p =>
                {
                    p("$k", t.Key); p("$a", t.CycleAnchor.ToString(Iso));
                    p("$i", t.IntervalMinutes); p("$r", t.RecurringIntervalMinutes);
                    p("$run", t.IsRunning ? 1 : 0);
                });
        }

        for (var i = 0; i < incident.Files.Count; i++)
        {
            var f = incident.Files[i];
            Run(cn, tx,
                "INSERT INTO incident_files (id, ordinal, file_name, content_type, size_bytes, added_at, added_by, display_name) VALUES ($id,$o,$fn,$ct,$sz,$at,$by,$dn);",
                p =>
                {
                    p("$id", f.Id.ToString()); p("$o", i); p("$fn", f.FileName);
                    p("$ct", f.ContentType); p("$sz", f.SizeBytes);
                    p("$at", f.AddedAt.ToString(Iso)); p("$by", f.AddedBy); p("$dn", f.DisplayName);
                });
        }

        tx.Commit();
    }

    private static void WriteChecklist(SqliteConnection cn, SqliteTransaction tx, IReadOnlyList<Domain.ChecklistItem> items, int kind)
    {
        for (var i = 0; i < items.Count; i++)
        {
            var c = items[i];
            Run(cn, tx,
                "INSERT INTO checklist_items (id, ordinal, text, is_done, note, is_mandatory, kind) VALUES ($id,$o,$t,$d,$n,$m,$k);",
                p =>
                {
                    p("$id", c.Id.ToString()); p("$o", i); p("$t", c.Text); p("$d", c.IsDone ? 1 : 0);
                    p("$n", (object?)c.Note ?? DBNull.Value); p("$m", c.IsMandatory ? 1 : 0); p("$k", kind);
                });
        }
    }

    /// <summary>
    /// Reads only the incident's lifecycle state, read-only and without migrating the file — meant
    /// for the Home overview's closed marker, which must not mutate files just to display them.
    /// `state` lives in the base schema's <c>incident_meta</c>, so this works across schema versions;
    /// any failure (missing, corrupt, locked, too new) returns null so the overview degrades quietly.
    /// </summary>
    public IncidentState? TryReadState(string path)
    {
        if (!File.Exists(path))
            return null;
        try
        {
            using var cn = SqliteConnectionFactory.OpenReadOnly(path);
            using var cmd = cn.CreateCommand();
            cmd.CommandText = "SELECT state FROM incident_meta LIMIT 1;";
            var raw = cmd.ExecuteScalar();
            return raw is null ? null : (IncidentState)Convert.ToInt32(raw);
        }
        catch
        {
            return null;
        }
    }

    public Incident Load(string path)
    {
        // Check before opening: SQLite would otherwise report a missing file as a bare "unable to
        // open database file", and the caller cannot tell that apart from a corrupt one.
        if (!File.Exists(path))
            throw new FileNotFoundException("Die Datei wurde nicht gefunden.", path);

        // Bring an older file up to the current schema before reading it. A file last written
        // by an earlier version sits at its old schema_version; without this, Load would query
        // tables that don't exist yet. Migration is version-gated/idempotent and only adds empty
        // tables, so re-opening a closed incident read-only afterwards still reads no new content.
        using (var migrateCn = SqliteConnectionFactory.OpenExisting(path))
        {
            Migrations.Migrate(migrateCn);
        }

        // Peek at state with a read-only connection to decide open mode.
        IncidentState state;
        using (var probe = SqliteConnectionFactory.OpenReadOnly(path))
        using (var cmd = probe.CreateCommand())
        {
            cmd.CommandText = "SELECT state FROM incident_meta LIMIT 1;";
            var raw = cmd.ExecuteScalar() ?? throw new InvalidOperationException("No incident in file.");
            state = (IncidentState)Convert.ToInt32(raw);
        }

        using var cn = state == IncidentState.Closed
            ? SqliteConnectionFactory.OpenReadOnly(path)
            : SqliteConnectionFactory.OpenExisting(path);

        var meta = ReadRow(cn,
            "SELECT id, started_at, state, incident_number, ils_number, keyword, street, district, status, closed_at, closed_by FROM incident_meta LIMIT 1;");

        var checklistAufbau = ReadAll(cn,
            "SELECT id, text, is_done, note, is_mandatory FROM checklist_items WHERE kind = 0 ORDER BY ordinal;",
            r => Domain.ChecklistItem.Rehydrate(Guid.Parse(r.GetString(0)), r.GetString(1), r.GetInt32(2) != 0, Str(r, 3), r.GetInt32(4) != 0));
        var checklistAbbau = ReadAll(cn,
            "SELECT id, text, is_done, note, is_mandatory FROM checklist_items WHERE kind = 1 ORDER BY ordinal;",
            r => Domain.ChecklistItem.Rehydrate(Guid.Parse(r.GetString(0)), r.GetString(1), r.GetInt32(2) != 0, Str(r, 3), r.GetInt32(4) != 0));

        var editsByEntry = ReadAll(cn,
            "SELECT entry_id, previous_text, edited_by, edited_at FROM etb_entry_edits ORDER BY ordinal;",
            r => (EntryId: Guid.Parse(r.GetString(0)),
                  Edit: new Domain.Etb.EtbEntryEdit(r.GetString(1), r.GetString(2), ParseDate(r.GetString(3)))))
            .GroupBy(x => x.EntryId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Edit).ToList());

        var journal = ReadAll(cn, "SELECT id, timestamp, direction, from_party, to_party, text, entered_by FROM etb_entries ORDER BY ordinal;",
            r =>
            {
                var id = Guid.Parse(r.GetString(0));
                return Domain.Etb.EtbEntry.Rehydrate(id, ParseDate(r.GetString(1)),
                    (Domain.Etb.EtbDirection)r.GetInt32(2), r.GetString(5), r.GetString(6), Str(r, 3), Str(r, 4),
                    editsByEntry.TryGetValue(id, out var eds) ? eds : null);
            });

        var roles = ReadAll(cn, "SELECT id, role, person_name, call_sign, from_time, to_time, section, phone FROM role_assignments ORDER BY ordinal;",
            r => new Domain.RoleAssignment(Guid.Parse(r.GetString(0)), r.GetString(1), r.GetString(2),
                Str(r, 3), NullableDate(r, 4), NullableDate(r, 5), Str(r, 6), Str(r, 7)));

        var forces = ReadAll(cn, "SELECT id, brigade, call_sign, personnel_count, scba_count, status, notes FROM force_units ORDER BY ordinal;",
            r => new Domain.ForceUnit(Guid.Parse(r.GetString(0)), r.GetString(1), Str(r, 2), r.GetInt32(3),
                r.GetInt32(4), Str(r, 5), Str(r, 6)));

        var membersByTrupp = ReadAll(cn,
            "SELECT trupp_id, role, name FROM scba_trupp_members ORDER BY ordinal;",
            r => (TruppId: Guid.Parse(r.GetString(0)),
                  Member: new Domain.Atemschutz.TruppMember(
                      (Domain.Atemschutz.TruppRole)r.GetInt32(1), r.GetString(2))))
            .GroupBy(x => x.TruppId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Member).ToList());

        var readingsByTrupp = ReadAll(cn,
            "SELECT trupp_id, reading_time, bar FROM scba_pressure_readings ORDER BY ordinal;",
            r => (TruppId: Guid.Parse(r.GetString(0)),
                  Reading: new Domain.Atemschutz.PressureReading(ParseDate(r.GetString(1)), r.GetInt32(2))))
            .GroupBy(x => x.TruppId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Reading).ToList());

        var scbaTrupps = ReadAll(cn,
            "SELECT id, designation, call_sign, task, registered_at, start_time, start_pressure, max_duration_minutes, return_pressure_bar, pressure_control_interval_minutes, exit_time FROM scba_trupps ORDER BY ordinal;",
            r =>
            {
                var id = Guid.Parse(r.GetString(0));
                return Domain.Atemschutz.AtemschutzTrupp.Rehydrate(
                    id, ParseDate(r.GetString(4)), NullableDate(r, 5), r.GetString(1),
                    membersByTrupp.TryGetValue(id, out var ms) ? ms : Enumerable.Empty<Domain.Atemschutz.TruppMember>(),
                    Str(r, 2), Str(r, 3), NullableInt(r, 6), r.GetInt32(7), r.GetInt32(8), r.GetInt32(9),
                    NullableDate(r, 10),
                    readingsByTrupp.TryGetValue(id, out var rs) ? rs : Enumerable.Empty<Domain.Atemschutz.PressureReading>());
            });

        var audit = ReadAll(cn, "SELECT at, action, by_operator FROM audit_events ORDER BY ordinal;",
            r => new Domain.AuditEvent(ParseDate(r.GetString(0)), r.GetString(1), r.GetString(2)));

        var timers = ReadAll(cn,
            "SELECT key, cycle_anchor, interval_minutes, recurring_interval_minutes, is_running FROM incident_timers;",
            r => new Domain.Time.IncidentTimerState(
                r.GetString(0), ParseDate(r.GetString(1)), r.GetInt32(2), r.GetInt32(3), r.GetInt32(4) != 0));

        var files = ReadAll(cn,
            "SELECT id, file_name, content_type, size_bytes, added_at, added_by, display_name FROM incident_files ORDER BY ordinal;",
            // display_name is null on rows written before this column existed -- fall back to
            // file_name, same idiom as the Einsatznummer legacy fallback just below.
            r => Domain.Files.IncidentFile.Rehydrate(Guid.Parse(r.GetString(0)), r.GetString(1), Str(r, 6) ?? r.GetString(1),
                r.GetString(2), r.GetInt64(3), ParseDate(r.GetString(4)), r.GetString(5)));

        // Legacy fallback: files written before the Einsatznummer unification carry the 4-digit
        // number in ils_number and nothing in incident_number. Load that old value as the
        // Einsatznummer so pre-existing incidents keep their number.
        var incidentNumber =
            meta[3] is string n ? new Domain.ValueObjects.IncidentNumber(n)
            : meta[4] is string legacyIls ? new Domain.ValueObjects.IncidentNumber(legacyIls)
            : null;

        return Incident.Rehydrate(
            Guid.Parse((string)meta[0]!),
            ParseDate((string)meta[1]!),
            (IncidentState)Convert.ToInt32(meta[2]),
            incidentNumber,
            meta[5] as string,
            meta[6] as string,
            meta[7] as string,
            meta[8] as string,
            meta[9] is string ca ? ParseDate(ca) : null,
            meta[10] as string,
            checklistAufbau, checklistAbbau, journal, roles, forces, scbaTrupps, audit, timers, files);
    }

    private static DateTimeOffset ParseDate(string s) =>
        DateTimeOffset.Parse(s, null, System.Globalization.DateTimeStyles.RoundtripKind);

    private static string? Str(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    private static int? NullableInt(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetInt32(i);

    private static DateTimeOffset? NullableDate(SqliteDataReader r, int i) =>
        r.IsDBNull(i) ? null : ParseDate(r.GetString(i));

    private static object?[] ReadRow(SqliteConnection cn, string sql)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandText = sql;
        using var r = cmd.ExecuteReader();
        if (!r.Read()) throw new InvalidOperationException("No incident in file.");
        var values = new object?[r.FieldCount];
        for (var i = 0; i < r.FieldCount; i++)
            values[i] = r.IsDBNull(i) ? null : r.GetValue(i);
        return values;
    }

    private static List<T> ReadAll<T>(SqliteConnection cn, string sql, Func<SqliteDataReader, T> map)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandText = sql;
        using var r = cmd.ExecuteReader();
        var list = new List<T>();
        while (r.Read()) list.Add(map(r));
        return list;
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
