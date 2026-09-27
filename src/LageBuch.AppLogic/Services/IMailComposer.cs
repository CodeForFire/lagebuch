namespace LageBuch.AppLogic.Services;

/// <summary>
/// Platform hook for handing a finished PDF to the operating system's mail program. Only heads that
/// can render a PDF have anything to attach, so Android supplies <see cref="NoopMailComposer"/> and
/// the close dialog does not offer the option.
/// </summary>
public interface IMailComposer
{
    /// <summary>Whether this platform can open a mail program at all (false hides the option).</summary>
    bool CanCompose { get; }

    Task<MailComposeResult> ComposeAsync(MailDraft draft, CancellationToken cancellationToken = default);
}

/// <summary>A new e-mail to open in the Lagebuchführer's own mail program; the recipient is left to them.</summary>
public sealed record MailDraft(string Subject, string Body, string AttachmentPath);

/// <summary>How far <see cref="IMailComposer.ComposeAsync"/> got.</summary>
public enum MailComposeResult
{
    /// <summary>The mail program opened a new message with the PDF attached.</summary>
    Attached,

    /// <summary>A new message opened without the PDF (plain <c>mailto:</c>); the folder holding it was shown.</summary>
    OpenedWithoutAttachment,

    /// <summary>No mail program could be opened.</summary>
    Failed,
}

/// <summary>No-op composer for heads without a mail hand-off; <see cref="CanCompose"/> is false.</summary>
public sealed class NoopMailComposer : IMailComposer
{
    public bool CanCompose => false;

    public Task<MailComposeResult> ComposeAsync(MailDraft draft, CancellationToken cancellationToken = default) =>
        Task.FromResult(MailComposeResult.Failed);
}
