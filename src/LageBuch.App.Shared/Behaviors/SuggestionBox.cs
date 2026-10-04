using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace LageBuch.App.Shared.Behaviors;

/// <summary>
/// The keyboard model every <see cref="AutoCompleteBox"/> in this app keeps (#466), switched on
/// once by a Setter on the shared "AutoCompleteBox" style rather than per view.
///
/// <list type="bullet">
/// <item>The suggestion list opens on focus and on a click (#259), not only on the first
/// keystroke. Every box has MinimumPrefixLength="0", so it is willing to show its full list for an
/// empty string; Avalonia just never asks it to until the text changes.</item>
/// <item>A single match that starts with what was typed is completed inline, the added text
/// selected: typing on or Backspace drops it, Tab and Enter take it. Funkrufnamen are prefixes of
/// each other ("Florian X 1/46" of "Florian X 1/46-1"), so a match is never taken unseen, never on a
/// substring and never while several are left. Not on Android, where an IME and a programmatic
/// selection fight and there is no Tab to take it.</item>
/// <item>Shift+Tab never takes a match: it puts back what was typed and moves on.</item>
/// </list>
///
/// The rest of the key table is Avalonia's own, measured in KeyboardRuntimeFactsTests: Tab keeps
/// the text the box shows, Esc closes the list and puts back what was typed. Enter takes a pending
/// pick (see <see cref="ClaimsEnter"/>) and otherwise belongs to the form, which asks this class.
/// </summary>
public static class SuggestionBox
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<AutoCompleteBox, bool>("IsEnabled", typeof(SuggestionBox));

    public static void SetIsEnabled(AutoCompleteBox target, bool value)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetValue(IsEnabledProperty, value);
    }

    public static bool GetIsEnabled(AutoCompleteBox target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.GetValue(IsEnabledProperty);
    }

    /// <summary>
    /// Whether Enter belongs to the box: its list is open and it shows text the user did not type —
    /// a row arrowed onto, or an inline completion. Enter then takes that text and must not also
    /// submit the form, or picking a Truppmann would create the Trupp. Otherwise Enter submits in one
    /// press, list open or not. Avalonia keeps what was typed in <see cref="AutoCompleteBox.SearchText"/>.
    /// </summary>
    public static bool ClaimsEnter(AutoCompleteBox box)
    {
        ArgumentNullException.ThrowIfNull(box);
        return box.IsDropDownOpen && HasPendingPick(box);
    }

    /// <summary>Closes the list of the suggestion box <paramref name="source"/> sits in, if any.</summary>
    public static void CloseList(StyledElement source)
    {
        ArgumentNullException.ThrowIfNull(source);
        for (StyledElement? current = source; current is not null; current = current.TemplatedParent as StyledElement)
        {
            if (current is AutoCompleteBox { IsDropDownOpen: true } box)
            {
                box.SetCurrentValue(AutoCompleteBox.IsDropDownOpenProperty, false);
                return;
            }
        }
    }

    static SuggestionBox()
    {
        IsEnabledProperty.Changed.AddClassHandler<AutoCompleteBox>((box, e) =>
        {
            box.RemoveHandler(InputElement.GotFocusEvent, OnGotFocus);
            box.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
            box.RemoveHandler(InputElement.KeyDownEvent, OnPreviewKeyDown);
            box.TemplateApplied -= OnTemplateApplied;
            if (e.NewValue is true)
            {
                box.AddHandler(InputElement.GotFocusEvent, OnGotFocus);

                // Tunnel + handledEventsToo: the inner TextBox handles PointerPressed itself (to
                // place the caret) and marks it handled, which would otherwise stop a bubbling
                // handler from ever seeing the click.
                box.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);

                // Tunnel: keyboard navigation moves focus on the bubbling Tab, after which the box
                // has already closed its list.
                box.AddHandler(InputElement.KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);

                if (!OperatingSystem.IsAndroid())
                {
                    box.TemplateApplied += OnTemplateApplied;
                }
            }
        });
    }

    private static bool HasPendingPick(AutoCompleteBox box) =>
        !string.Equals(box.Text ?? string.Empty, box.SearchText ?? string.Empty, StringComparison.Ordinal);

    private static void OnGotFocus(object? sender, RoutedEventArgs e) => Open(sender);

    private static void OnPointerPressed(object? sender, PointerPressedEventArgs e) => Open(sender);

    private static void Open(object? sender)
    {
        if (sender is AutoCompleteBox { IsDropDownOpen: false } box)
        {
            box.IsDropDownOpen = true;
        }
    }

    private static void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Tab && e.KeyModifiers == KeyModifiers.Shift
            && sender is AutoCompleteBox { IsDropDownOpen: true } box && HasPendingPick(box))
        {
            box.SetCurrentValue(AutoCompleteBox.TextProperty, box.SearchText);
        }
    }

    private static void OnTemplateApplied(object? sender, TemplateAppliedEventArgs e)
    {
        if (e.NameScope.Find<TextBox>("PART_TextBox") is { } textBox)
        {
            textBox.TextChanged -= OnTextChanged;
            textBox.TextChanged += OnTextChanged;
        }
    }

    // Avalonia's own text completion takes the first match that starts with the text, even with
    // several left ("Must" → "Mustermann" next to "Musterfrau"). So it is switched on only for the
    // one case this box allows. This runs before the box reacts: it handles a text change on a
    // posted job.
    private static void OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is TextBox { TemplatedParent: AutoCompleteBox box } textBox && GetIsEnabled(box))
        {
            box.IsTextCompletionEnabled = CompletesTo(box, textBox.Text) is not null;
        }
    }

    // The single item the box's own filter leaves for the text, when it starts with that text and
    // adds something to it.
    private static string? CompletesTo(AutoCompleteBox box, string? text)
    {
        if (string.IsNullOrEmpty(text) || box.TextFilter is not { } filter || box.ItemsSource is null)
        {
            return null;
        }

        string? only = null;
        foreach (var item in box.ItemsSource)
        {
            var value = item?.ToString();
            if (value is null || !filter(text, value))
            {
                continue;
            }

            if (only is not null)
            {
                return null;
            }

            only = value;
        }

        return only is not null && only.Length > text.Length && only.StartsWith(text, StringComparison.OrdinalIgnoreCase) ? only : null;
    }
}
