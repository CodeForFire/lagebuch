using System.Security.Cryptography;
using LageBuch.AppLogic.Services;
using LageBuch.Domain;
using LageBuch.Domain.CoMeasurement;
using LageBuch.Domain.Etb;
using LageBuch.Domain.Files;
using LageBuch.Domain.Time;
using LageBuch.Persistence;
using Microsoft.Data.Sqlite;

namespace LageBuch.AppLogic.Tests;

// The seal of a closed Einsatz (#598): computed at close, stored in the file, checked on open.
public sealed class IncidentSealerTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));

    private static readonly Guid IncidentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid FileId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid HouseA = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid HouseB = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid EntryId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private static readonly string PhotoSha = Convert.ToHexStringLower(SHA256.HashData("foto"u8));

    private readonly string _dir = Directory.CreateTempSubdirectory("lagebuch-seal-").FullName;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    // ----- The canonical v1 input -------------------------------------------------------------
    // Pins the v1 format: if this hash changes, every seal already printed in a Bericht stops
    // verifying. A deliberate change is a new version, never an edit of this literal.
    [Fact]
    public void The_v1_seal_of_a_fixed_incident_is_stable()
    {
        var seal = IncidentSealer.Compute(Golden(), new Dictionary<Guid, string?> { [FileId] = PhotoSha });

        Assert.Equal(IncidentSeal.CurrentVersion, seal.Version);
        Assert.Equal("d02c07dd8fdea14132a1320d30663e9e928b8953c2de738074bae5c311ab9d97", seal.Hash);
    }

    [Fact]
    public void The_canonical_input_starts_with_the_version_and_ends_with_one_line_per_attachment()
    {
        var input = IncidentSealer.CanonicalInput(Golden(), new Dictionary<Guid, string?> { [FileId] = null });

        Assert.StartsWith("lagebuch-seal-v1\n{", input, StringComparison.Ordinal);
        Assert.EndsWith($"\n{FileId} -\n", input, StringComparison.Ordinal);
    }

    [Fact]
    public void The_sync_counters_do_not_enter_the_seal()
    {
        var input = IncidentSealer.CanonicalInput(Golden(), new Dictionary<Guid, string?>());

        Assert.DoesNotContain("\"revision\"", input, StringComparison.Ordinal);
        Assert.DoesNotContain("\"epoch\"", input, StringComparison.Ordinal);
    }

    // The four ways the incident in memory at close and the one read back from SQLite differ
    // without differing in content. Each must leave the seal alone.
    [Fact]
    public void A_stale_building_ordinal_does_not_change_the_seal()
    {
        Assert.Equal(Hash(Golden()), Hash(Golden(ordinalA: 5, ordinalB: 9)));
    }

    [Fact]
    public void The_order_of_the_dwellings_does_not_change_the_seal()
    {
        Assert.Equal(Hash(Golden()), Hash(Golden(dwellingsReversed: true)));
    }

    [Fact]
    public void The_order_of_the_timers_does_not_change_the_seal()
    {
        Assert.Equal(Hash(Golden()), Hash(Golden(timersReversed: true)));
    }

    [Fact]
    public void The_insertion_order_of_a_dictionary_does_not_change_the_seal()
    {
        Assert.Equal(Hash(Golden()), Hash(Golden(descriptionsReversed: true)));
    }

    [Fact]
    public void A_changed_word_changes_the_seal()
    {
        Assert.NotEqual(Hash(Golden()), Hash(Golden(entryText: "Einsatz begonnen!")));
    }

    [Fact]
    public void A_changed_attachment_hash_changes_the_seal()
    {
        var a = IncidentSealer.Compute(Golden(), new Dictionary<Guid, string?> { [FileId] = PhotoSha });
        var b = IncidentSealer.Compute(Golden(), new Dictionary<Guid, string?> { [FileId] = null });

        Assert.NotEqual(a.Hash, b.Hash);
    }

    // ----- Close, save, reload, verify ---------------------------------------------------------
    [Fact]
    public async Task Close_seals_the_incident_including_attachment_hashes()
    {
        var store = new FakeStore();
        var session = StartSession(store, out _);
        await session.AddFileAsync("foto.jpg", "image/jpeg", "foto"u8.ToArray());
        var file = session.Incident.Files[0];

        session.Close();

        var seal = Assert.IsType<IncidentSeal>(session.Incident.Seal);
        Assert.Equal(PhotoSha, seal.FileSha256[file.Id]);
        Assert.Equal(IncidentSealer.Compute(session.Incident, seal.FileSha256).Hash, seal.Hash);
    }

    [Fact]
    public async Task The_seal_matches_after_save_and_reload_with_every_determinism_trap_in_the_incident()
    {
        var path = Path.Join(_dir, "einsatz.fwincident");
        var store = new IncidentStore();
        var session = StartSession(store, out _, path);
        await FillWithEveryTrap(session);
        session.Close();
        var sealedInMemory = Assert.IsType<IncidentSeal>(session.Incident.Seal);
        await store.FlushAsync();

        var reopened = LocalIncidentSession.Open(store, new FixedClock(T0), path, op: null);
        var check = await reopened.VerifySealAsync();

        Assert.Equal(sealedInMemory.Hash, reopened.Incident.Seal?.Hash);
        Assert.Equal(SealCheckStatus.Intact, check.Status);
        Assert.Null(check.Detail);
    }

    [Fact]
    public async Task An_etb_text_edited_in_the_database_breaks_the_seal()
    {
        var path = await SealedFileOnDisk();
        Exec(path, "UPDATE etb_entries SET text = 'Lagemeldung geändert' WHERE text = 'Lagemeldung';");

        var check = await LocalIncidentSession.Open(new IncidentStore(), new FixedClock(T0), path, op: null).VerifySealAsync();

        Assert.Equal(SealCheckStatus.Broken, check.Status);
        Assert.Equal("Inhalt weicht vom Siegel ab", check.Detail);
    }

    [Fact]
    public async Task An_attachment_hash_rewritten_in_the_database_together_with_the_bytes_still_breaks_the_seal()
    {
        var path = await SealedFileOnDisk();
        var forged = Convert.ToHexStringLower(SHA256.HashData("fälschung"u8));
        await File.WriteAllBytesAsync(AttachmentPath(path), "fälschung"u8.ToArray());
        Exec(path, $"UPDATE incident_files SET sha256 = '{forged}';");

        var check = await LocalIncidentSession.Open(new IncidentStore(), new FixedClock(T0), path, op: null).VerifySealAsync();

        Assert.Equal(SealCheckStatus.Broken, check.Status);
        Assert.Equal("Inhalt weicht vom Siegel ab", check.Detail);
    }

    [Fact]
    public async Task A_changed_attachment_breaks_the_seal_and_is_named()
    {
        var path = await SealedFileOnDisk();
        await File.WriteAllBytesAsync(AttachmentPath(path), "anderes foto"u8.ToArray());

        var check = await LocalIncidentSession.Open(new IncidentStore(), new FixedClock(T0), path, op: null).VerifySealAsync();

        Assert.Equal(SealCheckStatus.Broken, check.Status);
        Assert.Equal("Anhang verändert: foto.jpg", check.Detail);
    }

    [Fact]
    public async Task A_missing_attachment_breaks_the_seal_and_is_named()
    {
        var path = await SealedFileOnDisk();
        File.Delete(AttachmentPath(path));

        var check = await LocalIncidentSession.Open(new IncidentStore(), new FixedClock(T0), path, op: null).VerifySealAsync();

        Assert.Equal(SealCheckStatus.Broken, check.Status);
        Assert.Equal("Anhang fehlt: foto.jpg", check.Detail);
    }

    [Fact]
    public async Task A_file_closed_before_the_seal_reports_not_sealed()
    {
        var path = Path.Join(_dir, "alt.fwincident");
        var clock = new FixedClock(T0);
        var op = new SessionOperator("Müller");
        var incident = Incident.Start(clock, op);
        incident.Close(clock, op);
        IncidentRepository.Save(path, incident);

        var check = await LocalIncidentSession.Open(new IncidentStore(), clock, path, op: null).VerifySealAsync();

        Assert.Equal(SealCheckStatus.NotSealed, check.Status);
    }

    [Fact]
    public void A_seal_in_an_unknown_format_cannot_be_verified()
    {
        var incident = Golden();
        incident.ApplySeal(new IncidentSeal("lagebuch-seal-v9", "abc", new Dictionary<Guid, string?>()));

        var check = IncidentSealer.Verify(incident, new Dictionary<Guid, string?>());

        Assert.Equal(SealCheckStatus.Unverifiable, check.Status);
        Assert.Equal("Siegelformat unbekannt: lagebuch-seal-v9", check.Detail);
    }

    [Fact]
    public async Task The_store_carries_the_seal_through_its_snapshot_copy()
    {
        Incident? written = null;
        var store = new IncidentStore((_, incident) => written = incident);
        var incident = Golden();
        var seal = IncidentSealer.Compute(incident, new Dictionary<Guid, string?> { [FileId] = PhotoSha });
        incident.ApplySeal(seal);

        store.Save("/x.fwincident", incident);
        await store.FlushAsync();

        Assert.NotNull(written);
        Assert.Equal(seal.StoredForm, written.Seal?.StoredForm);
        Assert.Equal(PhotoSha, written.Seal?.FileSha256[FileId]);
    }

    private static string Hash(Incident incident) =>
        IncidentSealer.Compute(incident, new Dictionary<Guid, string?> { [FileId] = PhotoSha }).Hash;

    private static LocalIncidentSession StartSession(IIncidentStore store, out FixedClock clock, string path = "/x.fwincident")
    {
        clock = new FixedClock(T0);
        return TestSession.StartNew(
            store,
            clock,
            new SessionOperator("Müller", "FFB 12/1"),
            path,
            new[] { ("Blaulicht aus?", true), ("Bei ILS gemeldet?", false) },
            new[] { ("Fahrzeug abgerüstet?", true) });
    }

    // Everything that differs between the aggregate in memory and the one SQLite hands back:
    // a removed Haus leaves the others' ordinals stale, dwellings of two houses interleave by
    // floor on reload, a re-upserted timer moves to the end of the list, and dictionaries are
    // filled out of key order.
    private static async Task FillWithEveryTrap(LocalIncidentSession session)
    {
        session.AddJournalEntry(EtbDirection.Incoming, "Lagemeldung", from: "ILS");
        session.AddCoBuilding("Haus 1", 2, 2);
        session.AddCoBuilding("Haus 2", 2, 2, undergroundFloorCount: 1);
        session.AddCoBuilding("Haus 3", 1, 1);
        session.RemoveCoBuilding(session.Incident.Buildings[0].Id);
        var house2 = session.Incident.Buildings[0].Id;
        var house3 = session.Incident.Buildings[1].Id;
        session.SetFloorDescription(house2, 2, "Dachgeschoss");
        session.SetFloorDescription(house2, -1, "Keller");
        session.SetFloorDescription(house2, 0, "Laden");
        session.SetApartmentLabel(house2, 1, 2, "Rechts");
        session.SetApartmentLabel(house2, 1, 1, "Links");
        session.RecordCoValue(house3, 0, 1, 30);
        session.RecordCoValue(house2, 0, 1, 12);
        session.UpsertTimer("zeta", T0, 10, 10, isRunning: true);
        session.UpsertTimer("alpha", T0, 5, 5, isRunning: false);
        session.UpsertTimer("zeta", T0.AddMinutes(10), 10, 10, isRunning: true);
        await session.AddFileAsync("foto.jpg", "image/jpeg", "foto"u8.ToArray());
        await session.AddFileAsync("plan.pdf", "application/pdf", "%PDF-plan"u8.ToArray());
    }

    private async Task<string> SealedFileOnDisk()
    {
        var path = Path.Join(_dir, "einsatz.fwincident");
        var store = new IncidentStore();
        var session = StartSession(store, out _, path);
        session.AddJournalEntry(EtbDirection.Incoming, "Lagemeldung", from: "ILS");
        await session.AddFileAsync("foto.jpg", "image/jpeg", "foto"u8.ToArray());
        session.Close();
        await store.FlushAsync();
        SqliteConnection.ClearAllPools();
        return path;
    }

    private static string AttachmentPath(string incidentPath)
    {
        var folder = Path.Join(Path.GetDirectoryName(incidentPath), Path.GetFileNameWithoutExtension(incidentPath) + ".files");
        return Assert.Single(Directory.GetFiles(folder));
    }

    private static void Exec(string path, string sql)
    {
        using (var cn = new SqliteConnection($"Data Source={path}"))
        {
            cn.Open();
            using var cmd = cn.CreateCommand();
#pragma warning disable CA2100 // test-only: the statements are literals in this file
            cmd.CommandText = sql;
#pragma warning restore CA2100
            Assert.True(cmd.ExecuteNonQuery() > 0, $"tamper statement changed nothing: {sql}");
        }

        SqliteConnection.ClearAllPools();
    }

    private static Incident Golden(
        int ordinalA = 0,
        int ordinalB = 1,
        bool dwellingsReversed = false,
        bool timersReversed = false,
        bool descriptionsReversed = false,
        string entryText = "Einsatz begonnen")
    {
        var descriptions = new List<KeyValuePair<int, string?>> { new(1, "OG"), new(0, "EG") };
        if (descriptionsReversed)
        {
            descriptions.Reverse();
        }

        var dwellings = new List<Dwelling>
        {
            Dwelling.Rehydrate(Guid.Parse("66666666-6666-6666-6666-666666666661"), HouseA, 0, 1, "Muster", DwellingStatus.Searched, true, 12),
            Dwelling.Rehydrate(Guid.Parse("66666666-6666-6666-6666-666666666662"), HouseB, 0, 1, null, DwellingStatus.NotSearched, null, null),
            Dwelling.Rehydrate(Guid.Parse("66666666-6666-6666-6666-666666666663"), HouseA, 1, 1, null, DwellingStatus.Affected, false, 30),
        };
        if (dwellingsReversed)
        {
            dwellings.Reverse();
        }

        var timers = new List<IncidentTimerState>
        {
            new("ils", T0, 10, 10, true),
            new("agt", T0, 5, 5, false),
        };
        if (timersReversed)
        {
            timers.Reverse();
        }

        return Incident.Rehydrate(
            IncidentId,
            T0,
            IncidentState.Closed,
            new Domain.ValueObjects.IncidentNumber("B 1.2 260715 4242"),
            "B3P",
            "Musterstraße 1",
            "Musterstadt",
            null,
            T0.AddHours(2),
            "Müller",
            [],
            [EtbEntry.Rehydrate(EntryId, T0, EtbDirection.System, entryText, "Müller", null, null)],
            [],
            [],
            [],
            [new AuditEvent(T0, "opened", "Müller"), new AuditEvent(T0.AddHours(2), "closed", "Müller")],
            timers,
            [IncidentFile.Rehydrate(FileId, "foto.jpg", "Foto Lage", "image/jpeg", 4, T0, "Müller")],
            [],
            [
                Building.Rehydrate(HouseA, "Haus A", 1, 1, new Dictionary<int, string?>(descriptions), ordinalA),
                Building.Rehydrate(HouseB, "Haus B", 1, 1, new Dictionary<int, string?>(), ordinalB),
            ],
            dwellings,
            []);
    }
}
