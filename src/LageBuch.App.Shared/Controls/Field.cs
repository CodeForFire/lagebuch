using Avalonia;
using Avalonia.Controls.Primitives;

namespace LageBuch.App.Shared.Controls;

/// <summary>
/// The message slot on a labeled field (<c>HeaderedContentControl Classes="field"</c>). Set
/// <c>Field.Error</c> to a non-null string and the field's template shows it underneath the input;
/// null -- the normal state -- shows nothing (#412).
/// <para>
/// An attached property rather than a bespoke control, because the wrapper already exists and every
/// field in the application already goes through it. That is also what keeps the marking of
/// mandatory fields (#414) a style rule instead of a second pass over every view.
/// </para>
/// <para>
/// The message explains, it never blocks. A form shows it on a press that could not succeed and
/// leaves everything the operator typed exactly where it is.
/// </para>
/// </summary>
public static class Field
{
    /// <summary>The message shown under the field, or null while there is nothing to say.</summary>
    public static readonly AttachedProperty<string?> ErrorProperty =
        AvaloniaProperty.RegisterAttached<HeaderedContentControl, string?>("Error", typeof(Field));

    public static void SetError(HeaderedContentControl target, string? value)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetValue(ErrorProperty, value);
    }

    public static string? GetError(HeaderedContentControl target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.GetValue(ErrorProperty);
    }
}
