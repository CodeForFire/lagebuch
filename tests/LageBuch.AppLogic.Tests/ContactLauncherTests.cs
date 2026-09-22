using LageBuch.AppLogic.Services;

namespace LageBuch.AppLogic.Tests;

public class ContactLauncherTests
{
    [Fact]
    public async Task A_launched_address_reaches_the_platform_bare_and_unchanged()
    {
        var dialogs = new RecordingContactDialogs();

        var error = await ContactLauncher.TryMailAsync(dialogs, "kbi@example.org", "Mustermann, Max");

        Assert.Null(error);
        Assert.Equal("kbi@example.org", dialogs.MailedAddress);
    }

    [Fact]
    public async Task A_launched_number_reaches_the_platform_bare_and_unchanged()
    {
        var dialogs = new RecordingContactDialogs();

        var error = await ContactLauncher.TryCallAsync(dialogs, "01 71 / 6 53 58 23", "Mustermann, Max");

        Assert.Null(error);
        Assert.Equal("01 71 / 6 53 58 23", dialogs.DialedNumber);
    }

    // The assertion that matters is the second one: a refused value must never reach the OS.
    [Theory]
    [InlineData("a@b.de\r\nBcc: opfer@example.org")]
    [InlineData("a@b.de?bcc=opfer@example.org")]
    [InlineData("javascript:x@y.de")]
    [InlineData("nodomain")]
    [InlineData("")]
    [InlineData(null)]
    public async Task A_refused_address_never_reaches_the_launcher(string? address)
    {
        var dialogs = new RecordingContactDialogs();

        var error = await ContactLauncher.TryMailAsync(dialogs, address, "Mustermann, Max");

        Assert.Equal("„Mustermann, Max“ hat keine gültige E-Mail-Adresse.", error);
        Assert.Null(dialogs.MailedAddress);
    }

    [Theory]
    [InlineData("*#06#")]
    [InlineData("0171;phone-context=+49")]
    [InlineData("Notruf 112 waehlen")]
    [InlineData("12")]
    [InlineData("")]
    [InlineData(null)]
    public async Task A_refused_number_never_reaches_the_dialer(string? number)
    {
        var dialogs = new RecordingContactDialogs();

        var error = await ContactLauncher.TryCallAsync(dialogs, number, "Mustermann, Max");

        Assert.Equal("„Mustermann, Max“ hat keine gültige Telefonnummer.", error);
        Assert.Null(dialogs.DialedNumber);
    }

    [Fact]
    public async Task A_device_without_a_mail_app_is_reported_rather_than_crashing_the_caller()
    {
        var error = await ContactLauncher.TryMailAsync(
            new ThrowingContactDialogs(), "kbi@example.org", "Mustermann, Max");

        Assert.NotNull(error);
        Assert.Contains("Mustermann, Max", error, StringComparison.Ordinal);
        Assert.Contains("kein E-Mail-Programm gefunden", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_device_without_a_dialer_is_reported_rather_than_crashing_the_caller()
    {
        var error = await ContactLauncher.TryCallAsync(
            new ThrowingContactDialogs(), "01716535823", "Mustermann, Max");

        Assert.NotNull(error);
        Assert.Contains("kein Telefon gefunden", error, StringComparison.Ordinal);
    }

    // The banner names the person, not the address -- the address is already on screen beside the
    // button, and a name reads better in a message.
    [Fact]
    public async Task The_message_names_the_person_rather_than_the_address()
    {
        var error = await ContactLauncher.TryMailAsync(
            new ThrowingContactDialogs(), "sehr.lange.adresse@example.org", "Mustermann, Max");

        Assert.NotNull(error);
        Assert.DoesNotContain("example.org", error, StringComparison.Ordinal);
    }

    private class RecordingContactDialogs : IFileDialogService
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

        public virtual Task OpenMailAsync(string address)
        {
            MailedAddress = address;
            return Task.CompletedTask;
        }

        public virtual Task OpenPhoneAsync(string number)
        {
            DialedNumber = number;
            return Task.CompletedTask;
        }

        public Task ShareFileAsync(string path, string mimeType) => Task.CompletedTask;
    }

    private sealed class ThrowingContactDialogs : RecordingContactDialogs
    {
        public override Task OpenMailAsync(string address) =>
            throw new InvalidOperationException("kein E-Mail-Programm gefunden");

        public override Task OpenPhoneAsync(string number) =>
            throw new InvalidOperationException("kein Telefon gefunden");
    }
}
