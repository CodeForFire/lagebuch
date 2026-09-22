using System.Diagnostics.CodeAnalysis;

namespace LageBuch.AppLogic.Services;

/// <summary>
/// Opens a roster contact's address or number through <see cref="IFileDialogService"/> and turns
/// either failure — a value the validators refuse, or a platform launcher that throws — into the
/// German message a view model shows in place. The companion to <see cref="MailAddressValidator"/>
/// and <see cref="PhoneNumberValidator"/>, which decide what may be launched; this decides what the
/// user is told when it cannot be. Shaped exactly like <see cref="UrlLauncher"/>.
/// </summary>
public static class ContactLauncher
{
    /// <summary>
    /// Returns <c>null</c> when the address was handed to the platform launcher, or the message to
    /// show otherwise.
    /// </summary>
    /// <param name="dialogs">The platform launcher.</param>
    /// <param name="address">The roster address. Comes from an imported Stammdaten file, so it is
    /// validated here rather than trusted.</param>
    /// <param name="label">What to name in the message — the person, not the address: the address
    /// is on screen beside the button, and a name reads better in a banner.</param>
    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "Deliberately broad: any launcher failure (no mail app registered, an Android ActivityNotFoundException) surfaces in the view instead of crashing it.")]
    public static async Task<string?> TryMailAsync(IFileDialogService dialogs, string? address, string label)
    {
        ArgumentNullException.ThrowIfNull(dialogs);

        if (!MailAddressValidator.TryGetMailtoUri(address, out _))
        {
            return $"„{label}“ hat keine gültige E-Mail-Adresse.";
        }

        try
        {
            // The bare address, not the URI: OpenMailAsync owns the scheme by contract.
            await dialogs.OpenMailAsync(address!.Trim());
            return null;
        }
        catch (Exception ex)
        {
            return $"Die E-Mail an „{label}“ konnte nicht geöffnet werden: {ex.Message}";
        }
    }

    /// <inheritdoc cref="TryMailAsync"/>
    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "Deliberately broad: any launcher failure (no dialer on a tablet, an Android ActivityNotFoundException) surfaces in the view instead of crashing it.")]
    public static async Task<string?> TryCallAsync(IFileDialogService dialogs, string? number, string label)
    {
        ArgumentNullException.ThrowIfNull(dialogs);

        if (!PhoneNumberValidator.TryGetTelUri(number, out _))
        {
            return $"„{label}“ hat keine gültige Telefonnummer.";
        }

        try
        {
            await dialogs.OpenPhoneAsync(number!.Trim());
            return null;
        }
        catch (Exception ex)
        {
            return $"Die Telefonnummer von „{label}“ konnte nicht gewählt werden: {ex.Message}";
        }
    }
}
