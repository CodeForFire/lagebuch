using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.Tests;

public class ContactsViewModelTests
{
    // Shaped like the real roster: a Notiz is either a Fachgebiet or the Feuerwehren somebody
    // covers, and the last entry carries neither an address nor a Notiz, as the roster's
    // xltm-sourced people do.
    private static ContactsViewModel Vm(IFileDialogService? dialogs = null) => new(
        new[]
        {
            new Person("Mustermann", "Max", "KBR", "Land 1", "01 71 / 1 23 45 67", "max@example.org", "Kreisbrandrat"),
            new Person("Beispiel", "Bernd", "KBM", "Land 2/3", "01 71 / 2 34 56 78", "bernd@example.org", "KBM Gefahrgut, Fachberater Arbeits- und Gesundheitsschutz"),
            new Person("Musterhuber", "Sepp", "KBM", "Land 3/1", "01 71 / 3 45 67 89", "sepp@example.org", "Alling, Biburg, Eichenau, Emmering"),
            new Person("Vorlage", "Kim", "Jugend", null, "01 71 / 7 89 01 23", null, null),
        },
        dialogs ?? new NoopContactDialogs());

    [Fact]
    public void Everything_is_visible_until_something_is_typed()
    {
        Assert.Equal(4, Vm().VisibleContacts.Count);
    }

    // The point of the feature: the word is only in the Notiz.
    [Fact]
    public void A_term_matches_the_notiz()
    {
        var vm = Vm();
        vm.FilterText = "gefahrgut";

        Assert.Single(vm.VisibleContacts);
        Assert.Equal("Beispiel, Bernd", vm.VisibleContacts[0].DisplayName);
    }

    [Fact]
    public void A_term_matches_one_of_the_feuerwehren_a_kbm_covers()
    {
        var vm = Vm();
        vm.FilterText = "Biburg";

        Assert.Single(vm.VisibleContacts);
        Assert.Equal("Musterhuber, Sepp", vm.VisibleContacts[0].DisplayName);
    }

    // Every term has to match, but each may land in a different field and in any order -- a plain
    // Contains over the whole query would find none of these.
    [Theory]
    [InlineData("KBM Gefahrgut")]
    [InlineData("gefahrgut kbm")]
    [InlineData("beispiel gefahrgut")]
    [InlineData("GEFAHRGUT")]
    [InlineData("  gefahrgut   kbm  ")]
    public void Terms_may_arrive_in_any_order_and_across_fields(string query)
    {
        var vm = Vm();
        vm.FilterText = query;

        Assert.Single(vm.VisibleContacts);
    }

    // The fields are joined with a separator, so a term can never match the concatenation of two
    // adjacent ones.
    [Fact]
    public void A_term_does_not_match_across_a_field_boundary()
    {
        var vm = Vm();
        vm.FilterText = "KBRLand";

        Assert.Empty(vm.VisibleContacts);
    }

    // The roster writes numbers for a human to read, so the digits an operator would dial have to
    // match too.
    [Fact]
    public void The_dialled_digits_find_a_number_written_with_separators()
    {
        var vm = Vm();
        vm.FilterText = "01711234567";

        Assert.Single(vm.VisibleContacts);
        Assert.Equal("Mustermann, Max", vm.VisibleContacts[0].DisplayName);
    }

    [Fact]
    public void An_unmatched_term_empties_the_list_without_touching_the_roster()
    {
        var vm = Vm();
        vm.FilterText = "Höhenrettung";

        Assert.Empty(vm.VisibleContacts);
        Assert.Equal(4, vm.Contacts.Count);
        Assert.True(vm.IsFiltered);
        Assert.Equal("Kein Kontakt passt zu „Höhenrettung“.", vm.NoMatchesMessage);
    }

    [Fact]
    public void Clearing_the_filter_brings_everyone_back()
    {
        var vm = Vm();
        vm.FilterText = "gefahrgut";

        vm.ClearFilterCommand.Execute(null);

        Assert.Equal(4, vm.VisibleContacts.Count);
        Assert.False(vm.IsFiltered);
    }

    [Fact]
    public async Task Mailing_a_contact_hands_the_bare_address_to_the_platform()
    {
        var dialogs = new NoopContactDialogs();
        var vm = Vm(dialogs);

        await vm.MailCommand.ExecuteAsync(vm.Contacts[1]);

        Assert.Null(vm.ErrorMessage);
        Assert.Equal("bernd@example.org", dialogs.MailedAddress);
    }

    [Fact]
    public async Task Calling_a_contact_hands_the_bare_number_to_the_platform()
    {
        var dialogs = new NoopContactDialogs();
        var vm = Vm(dialogs);

        await vm.CallCommand.ExecuteAsync(vm.Contacts[0]);

        Assert.Null(vm.ErrorMessage);
        Assert.Equal("01 71 / 1 23 45 67", dialogs.DialedNumber);
    }

    [Fact]
    public async Task Mailing_a_contact_without_an_address_reports_it_and_launches_nothing()
    {
        var dialogs = new NoopContactDialogs();
        var vm = Vm(dialogs);

        await vm.MailCommand.ExecuteAsync(vm.Contacts[3]);

        Assert.Equal("„Vorlage, Kim“ hat keine gültige E-Mail-Adresse.", vm.ErrorMessage);
        Assert.Null(dialogs.MailedAddress);
    }

    // The roster is imported, not typed here, so a crafted Stammdaten file must not be able to
    // add a recipient nobody saw.
    [Fact]
    public async Task A_roster_address_carrying_a_header_injection_never_reaches_the_launcher()
    {
        var dialogs = new NoopContactDialogs();
        var vm = new ContactsViewModel(
            new[] { new Person("Böse", "Bea", null, null, null, "a@b.de\r\nBcc: opfer@example.org") },
            dialogs);

        await vm.MailCommand.ExecuteAsync(vm.Contacts[0]);

        Assert.Equal("„Böse, Bea“ hat keine gültige E-Mail-Adresse.", vm.ErrorMessage);
        Assert.Null(dialogs.MailedAddress);
    }

    [Fact]
    public async Task A_roster_number_carrying_a_ussd_code_never_reaches_the_dialer()
    {
        var dialogs = new NoopContactDialogs();
        var vm = new ContactsViewModel(
            new[] { new Person("Böse", "Bea", null, null, "*#06#") },
            dialogs);

        await vm.CallCommand.ExecuteAsync(vm.Contacts[0]);

        Assert.Equal("„Böse, Bea“ hat keine gültige Telefonnummer.", vm.ErrorMessage);
        Assert.Null(dialogs.DialedNumber);
    }

    // A blank field arrives from the importer as "" rather than null, so the view's per-field
    // visibility cannot be a null check.
    [Fact]
    public void A_blank_field_counts_as_absent_rather_than_present()
    {
        var person = new Person("Leer", "Lena", null, "   ", string.Empty, "  ", string.Empty);

        Assert.False(person.HasEmail);
        Assert.False(person.HasPhone);
        Assert.False(person.HasNote);
    }

    private sealed class NoopContactDialogs : IFileDialogService
    {
        public string? MailedAddress { get; private set; }

        public string? DialedNumber { get; private set; }

        public Task<string?> PickSaveAsync(string suggestedFileName, string? initialFolder = null) => Task.FromResult<string?>(null);

        public Task<string?> PickOpenAsync() => Task.FromResult<string?>(null);

        public Task<string?> PickExportPdfAsync(string suggestedFileName) => Task.FromResult<string?>(null);

        public Task<string?> PickImportJsonAsync() => Task.FromResult<string?>(null);

        public Task<string?> PickExportJsonAsync(string suggestedFileName) => Task.FromResult<string?>(null);

        public Task<string?> PickAttachmentAsync() => Task.FromResult<string?>(null);

        public Task OpenFileAsync(string path) => Task.CompletedTask;

        public Task OpenUrlAsync(string url) => Task.CompletedTask;

        public Task OpenMailAsync(string address)
        {
            MailedAddress = address;
            return Task.CompletedTask;
        }

        public Task OpenPhoneAsync(string number)
        {
            DialedNumber = number;
            return Task.CompletedTask;
        }

        public Task ShareFileAsync(string path, string mimeType) => Task.CompletedTask;
    }
}
