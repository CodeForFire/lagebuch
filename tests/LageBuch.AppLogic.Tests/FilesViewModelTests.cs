using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.Files;

namespace LageBuch.AppLogic.Tests;

public class FilesViewModelTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public async Task AddFile_reads_the_picked_path_uploads_and_renders_a_row()
    {
        var changes = 0;
        var clock = new FixedClock(T0);
        var session = LocalIncidentSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        var path = Path.Combine(Path.GetTempPath(), $"brand-{Guid.NewGuid():N}.jpg");
        await File.WriteAllBytesAsync(path, new byte[] { 1, 2, 3 });
        try
        {
            var dialogs = new FakeDialogs { AttachmentPath = path };
            var vm = new FilesViewModel(session, dialogs, () => changes++);

            Assert.True(vm.AddFileCommand.CanExecute(null));
            await vm.AddFileCommand.ExecuteAsync(null);

            var file = Assert.Single(session.Incident.Files);
            Assert.Equal(Path.GetFileName(path), file.FileName);
            Assert.Equal("image/jpeg", file.ContentType);
            var row = Assert.Single(vm.Files);
            Assert.Equal(file.Id, row.Id);
            Assert.True(row.IsImage);
            Assert.Equal(1, changes);
            Assert.Null(vm.ErrorMessage);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task AddFile_cancelled_picker_does_nothing()
    {
        var clock = new FixedClock(T0);
        var session = LocalIncidentSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Müller"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        var vm = new FilesViewModel(session, new FakeDialogs { AttachmentPath = null }, () => { });

        await vm.AddFileCommand.ExecuteAsync(null);

        Assert.Empty(session.Incident.Files);
        Assert.Empty(vm.Files);
    }

    [Fact]
    public async Task AddFile_surfaces_a_domain_rejection_as_an_error_instead_of_throwing()
    {
        var clock = new FixedClock(T0);
        var session = LocalIncidentSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Müller"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        var path = Path.Combine(Path.GetTempPath(), $"notes-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(path, "hello");
        try
        {
            var vm = new FilesViewModel(session, new FakeDialogs { AttachmentPath = path }, () => { });

            await vm.AddFileCommand.ExecuteAsync(null);

            Assert.Empty(session.Incident.Files);
            Assert.NotNull(vm.ErrorMessage);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task AddFile_rejects_an_oversized_file_without_reading_it_into_memory()
    {
        var clock = new FixedClock(T0);
        var session = LocalIncidentSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        var path = Path.Combine(Path.GetTempPath(), $"riesig-{Guid.NewGuid():N}.jpg");
        using (var fs = new FileStream(path, FileMode.CreateNew))
        {
            fs.SetLength(IncidentFile.MaxSizeBytes + 1); // sparse — no real disk write, so the test stays fast
        }

        try
        {
            var vm = new FilesViewModel(session, new FakeDialogs { AttachmentPath = path }, () => { });

            var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            await vm.AddFileCommand.ExecuteAsync(null);
            var allocatedDuring = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;

            Assert.Empty(session.Incident.Files);
            Assert.NotNull(vm.ErrorMessage);
            Assert.True(
                allocatedDuring < 10 * 1024 * 1024,
                $"expected the oversized file to be rejected without reading it into memory, but the call allocated {allocatedDuring} bytes");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MaxFileSizeHint_reflects_the_domain_cap()
    {
        Assert.Equal($"Max. {IncidentFile.MaxSizeBytes / (1024 * 1024)} MB pro Datei", FilesViewModel.MaxFileSizeHint);
    }

    [Fact]
    public void ReadOnly_session_disables_add()
    {
        var clock = new FixedClock(T0);
        var session = LocalIncidentSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Müller"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        session.Close();
        var vm = new FilesViewModel(session, new FakeDialogs(), () => { });

        Assert.True(vm.IsReadOnly);
        Assert.False(vm.AddFileCommand.CanExecute(null));
    }

    [Fact]
    public async Task OpenFile_writes_a_temp_copy_and_hands_it_to_the_dialog_service()
    {
        var clock = new FixedClock(T0);
        var session = LocalIncidentSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        await session.AddFileAsync("brand.jpg", "image/jpeg", new byte[] { 9, 9, 9 });
        var dialogs = new FakeDialogs();
        var vm = new FilesViewModel(session, dialogs, () => { });
        var row = Assert.Single(vm.Files);

        await vm.OpenFileCommand.ExecuteAsync(row);
        try
        {
            Assert.NotNull(dialogs.LastOpenedPath);
            Assert.Equal(new byte[] { 9, 9, 9 }, await File.ReadAllBytesAsync(dialogs.LastOpenedPath!));
            Assert.Null(vm.ErrorMessage);
        }
        finally
        {
            if (dialogs.LastOpenedPath is not null)
            {
                File.Delete(dialogs.LastOpenedPath);
            }
        }
    }

    [Fact]
    public void Row_seeds_DisplayName_from_the_file_name()
    {
        var clock = new FixedClock(T0);
        var session = LocalIncidentSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        session.Incident.AddFile(clock, session.Operator!, "brand.jpg", "image/jpeg", 10);

        var vm = new FilesViewModel(session, new FakeDialogs(), () => { });

        Assert.Equal("brand.jpg", Assert.Single(vm.Files).DisplayName);
    }

    [Fact]
    public void Editing_DisplayName_writes_through_to_the_domain()
    {
        var clock = new FixedClock(T0);
        var session = LocalIncidentSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        var file = session.Incident.AddFile(clock, session.Operator!, "brand.jpg", "image/jpeg", 10);
        var vm = new FilesViewModel(session, new FakeDialogs(), () => { });
        var row = Assert.Single(vm.Files);

        row.DisplayName = "Küchenbrand";

        Assert.Equal("Küchenbrand", session.Incident.Files.Single(f => f.Id == file.Id).DisplayName);
    }

    [Fact]
    public void Editing_DisplayName_on_a_readonly_session_is_ignored()
    {
        var clock = new FixedClock(T0);
        var session = LocalIncidentSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        session.Incident.AddFile(clock, session.Operator!, "brand.jpg", "image/jpeg", 10);
        session.Close();
        var vm = new FilesViewModel(session, new FakeDialogs(), () => { });
        var row = Assert.Single(vm.Files);

        row.DisplayName = "Küchenbrand"; // must not throw despite the closed incident

        Assert.Equal("brand.jpg", session.Incident.Files.Single().DisplayName);
    }

    [Fact]
    public void Sync_renders_files_already_present_when_the_session_was_opened()
    {
        // Mirrors EtbViewModel's tail-sync: files added before this VM existed (a reopen, or
        // another module's mutation) must still show up once constructed.
        var clock = new FixedClock(T0);
        var store = new FakeStore();
        var op = new SessionOperator("Müller", "FFB 12/1");
        var seed = LocalIncidentSession.StartNew(
            store,
            clock,
            op,
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        seed.Incident.AddFile(clock, op, "vorab.pdf", "application/pdf", 10);

        var vm = new FilesViewModel(seed, new FakeDialogs(), () => { });

        var row = Assert.Single(vm.Files);
        Assert.Equal("vorab.pdf", row.FileName);
    }

    // --- Removing an attachment is destructive, so it goes through the same confirm gate as
    // ForcesViewModel's unit removal (#262 UX follow-up) ---
    [Fact]
    public async Task Removing_a_row_asks_for_confirmation_before_touching_the_session()
    {
        var clock = new FixedClock(T0);
        var session = LocalIncidentSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        session.Incident.AddFile(clock, session.Operator!, "brand.jpg", "image/jpeg", 10);
        string? confirmMessage = null;
        Action? confirmAction = null;
        var vm = new FilesViewModel(session, new FakeDialogs(), () => { }, (message, onConfirmed) =>
        {
            confirmMessage = message;
            confirmAction = onConfirmed;
        });
        var row = Assert.Single(vm.Files);

        row.RemoveCommand.Execute(null);

        // Nothing happened yet — the host only recorded the request.
        Assert.Single(vm.Files);
        Assert.Single(session.Incident.Files);
        Assert.Contains("brand.jpg", confirmMessage, StringComparison.Ordinal);

        confirmAction!();
        await WaitUntilAsync(() => vm.Files.Count == 0);

        Assert.Empty(vm.Files);
        Assert.Empty(session.Incident.Files);
        Assert.Contains("Datei entfernt: brand.jpg", session.Incident.Journal[^1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Removing_a_row_without_an_injected_confirm_runs_immediately()
    {
        var clock = new FixedClock(T0);
        var session = LocalIncidentSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        session.Incident.AddFile(clock, session.Operator!, "brand.jpg", "image/jpeg", 10);
        var vm = new FilesViewModel(session, new FakeDialogs(), () => { }); // no requestConfirm supplied
        var row = Assert.Single(vm.Files);

        row.RemoveCommand.Execute(null);
        await WaitUntilAsync(() => vm.Files.Count == 0);

        Assert.Empty(vm.Files);
        Assert.Empty(session.Incident.Files);
    }

    [Fact]
    public void Rows_of_a_readonly_incident_cannot_remove_themselves()
    {
        var clock = new FixedClock(T0);
        var store = new FakeStore();
        var op = new SessionOperator("Müller", "FFB 12/1");
        var seed = LocalIncidentSession.StartNew(
            store,
            clock,
            op,
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        seed.Incident.AddFile(clock, op, "brand.jpg", "image/jpeg", 10);
        seed.Close();

        var ro = LocalIncidentSession.OpenReadOnly(store, clock, "/x.fwincident");
        var vm = new FilesViewModel(ro, new FakeDialogs(), () => { });

        var row = Assert.Single(vm.Files);
        Assert.False(row.RemoveCommand.CanExecute(null));
        row.RemoveCommand.Execute(null); // inert, not throwing
        Assert.Single(ro.Incident.Files);
    }

    // Regression test for the append-only-to-reconciliation change: a file removed by another
    // client (simulated by mutating the domain directly, the same way a host broadcast lands)
    // must disappear from this ViewModel's rows once Sync() runs, not just newly-added ones show up.
    [Fact]
    public void Sync_drops_a_row_removed_by_another_client()
    {
        var clock = new FixedClock(T0);
        var op = new SessionOperator("Müller", "FFB 12/1");
        var session = LocalIncidentSession.StartNew(
            new FakeStore(),
            clock,
            op,
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        var kept = session.Incident.AddFile(clock, op, "vorab.pdf", "application/pdf", 10);
        var removedElsewhere = session.Incident.AddFile(clock, op, "brand.jpg", "image/jpeg", 10);
        var vm = new FilesViewModel(session, new FakeDialogs(), () => { });
        Assert.Equal(2, vm.Files.Count);

        // Another client's RemoveFileCommand landed and the host broadcast a new snapshot — here
        // stood in for by mutating the domain directly and raising Changed, same shape as
        // RemoteIncidentSession.OnSnapshot.
        session.Incident.RemoveFile(clock, op, removedElsewhere.Id);
        vm.Sync();

        var row = Assert.Single(vm.Files);
        Assert.Equal(kept.Id, row.Id);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(5);
        }
    }
}
