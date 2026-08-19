using Feuerwehr.AppLogic.ViewModels;
using Feuerwehr.Domain;

namespace Feuerwehr.AppLogic.Tests;

public class FilesViewModelTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public async Task AddFile_reads_the_picked_path_uploads_and_renders_a_row()
    {
        var changes = 0;
        var clock = new FixedClock(T0);
        var session = LocalIncidentSession.StartNew(new FakeStore(), clock,
            new SessionOperator("Müller", "FFB 12/1"), "/x.fwincident", Array.Empty<string>());
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
        var session = LocalIncidentSession.StartNew(new FakeStore(), clock,
            new SessionOperator("Müller"), "/x.fwincident", Array.Empty<string>());
        var vm = new FilesViewModel(session, new FakeDialogs { AttachmentPath = null }, () => { });

        await vm.AddFileCommand.ExecuteAsync(null);

        Assert.Empty(session.Incident.Files);
        Assert.Empty(vm.Files);
    }

    [Fact]
    public async Task AddFile_surfaces_a_domain_rejection_as_an_error_instead_of_throwing()
    {
        var clock = new FixedClock(T0);
        var session = LocalIncidentSession.StartNew(new FakeStore(), clock,
            new SessionOperator("Müller"), "/x.fwincident", Array.Empty<string>());
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
    public void ReadOnly_session_disables_add()
    {
        var clock = new FixedClock(T0);
        var session = LocalIncidentSession.StartNew(new FakeStore(), clock,
            new SessionOperator("Müller"), "/x.fwincident", Array.Empty<string>());
        session.Close();
        var vm = new FilesViewModel(session, new FakeDialogs(), () => { });

        Assert.True(vm.IsReadOnly);
        Assert.False(vm.AddFileCommand.CanExecute(null));
    }

    [Fact]
    public async Task OpenFile_writes_a_temp_copy_and_hands_it_to_the_dialog_service()
    {
        var clock = new FixedClock(T0);
        var session = LocalIncidentSession.StartNew(new FakeStore(), clock,
            new SessionOperator("Müller", "FFB 12/1"), "/x.fwincident", Array.Empty<string>());
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
            if (dialogs.LastOpenedPath is not null) File.Delete(dialogs.LastOpenedPath);
        }
    }

    [Fact]
    public void Sync_renders_files_already_present_when_the_session_was_opened()
    {
        // Mirrors EtbViewModel's tail-sync: files added before this VM existed (a reopen, or
        // another module's mutation) must still show up once constructed.
        var clock = new FixedClock(T0);
        var store = new FakeStore();
        var op = new SessionOperator("Müller", "FFB 12/1");
        var seed = LocalIncidentSession.StartNew(store, clock, op, "/x.fwincident", Array.Empty<string>());
        seed.Incident.AddFile(clock, op, "vorab.pdf", "application/pdf", 10);

        var vm = new FilesViewModel(seed, new FakeDialogs(), () => { });

        var row = Assert.Single(vm.Files);
        Assert.Equal("vorab.pdf", row.FileName);
    }
}
