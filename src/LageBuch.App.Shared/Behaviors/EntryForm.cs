using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.AppLogic.ViewModels;

namespace LageBuch.App.Shared.Behaviors;

/// <summary>
/// The keyboard contract every entry dock keeps (#540, contract 2 of #545), set on the dock's
/// root. Enter in any single-line field runs <see cref="SubmitCommandProperty"/>. After the entry
/// is added, focus goes to <see cref="FirstFieldProperty"/>; after validation refuses it, focus
/// goes to the first field marked <c>invalid</c>.
///
/// Enter is left alone in a suggestion box with a pick pending (<see cref="SuggestionBox.ClaimsEnter"/>:
/// Enter takes the row there), in a ComboBox whose list is open, in multi-line text, on a button
/// (Enter presses it) and inside an inline overlay panel, which keeps its own primary command. A
/// suggestion box whose list is merely open submits in one press (#466) and closes the list. The
/// handler is <b>tunneling</b> for the same reason as <see cref="EnterSubmit"/>: it must read the
/// box before the box handles Enter and closes the list. A closed ComboBox therefore submits on
/// Enter, and Space or Alt+↓ still open it.
///
/// Whether a submit went through is known only to the view model, so the refocus listens to
/// <see cref="IEntryForm.EntrySubmitted"/>, which also covers a click on the add button. A field
/// the view model keeps between entries (a sticky one) needs nothing here: a TextBox reached by
/// Tab selects its text, so typing replaces it and Tab keeps it.
/// </summary>
public static class EntryForm
{
    public static readonly AttachedProperty<ICommand?> SubmitCommandProperty =
        AvaloniaProperty.RegisterAttached<Control, ICommand?>("SubmitCommand", typeof(EntryForm));

    /// <summary>Where focus goes after an entry is added; the first tab stop when unset or unavailable.</summary>
    public static readonly AttachedProperty<Control?> FirstFieldProperty =
        AvaloniaProperty.RegisterAttached<Control, Control?>("FirstField", typeof(EntryForm));

    // The view model a dock listens to while it is on screen. Held only while attached, so the
    // long-lived view model does not keep a view of a module that is no longer shown alive.
    private static readonly ConditionalWeakTable<Control, Subscription> Subscriptions = new();

    static EntryForm()
    {
        SubmitCommandProperty.Changed.AddClassHandler<Control>((root, e) =>
        {
            Detach(root);
            if (e.NewValue is ICommand)
            {
                Attach(root);
            }
        });
    }

    public static void SetSubmitCommand(Control target, ICommand? value)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetValue(SubmitCommandProperty, value);
    }

    public static ICommand? GetSubmitCommand(Control target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.GetValue(SubmitCommandProperty);
    }

    public static void SetFirstField(Control target, Control? value)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetValue(FirstFieldProperty, value);
    }

    public static Control? GetFirstField(Control target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.GetValue(FirstFieldProperty);
    }

    private static void Attach(Control root)
    {
        root.AddHandler(InputElement.KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        root.AttachedToVisualTree += OnAttached;
        root.DetachedFromVisualTree += OnDetached;
        root.DataContextChanged += OnDataContextChanged;
        Subscribe(root);
    }

    private static void Detach(Control root)
    {
        root.RemoveHandler(InputElement.KeyDownEvent, OnPreviewKeyDown);
        root.AttachedToVisualTree -= OnAttached;
        root.DetachedFromVisualTree -= OnDetached;
        root.DataContextChanged -= OnDataContextChanged;
        Unsubscribe(root);
    }

    private static void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Control root)
        {
            Subscribe(root);
        }
    }

    private static void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Control root)
        {
            Unsubscribe(root);
        }
    }

    private static void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (sender is Control root)
        {
            Subscribe(root);
        }
    }

    private static void Subscribe(Control root)
    {
        Unsubscribe(root);
        if (root.IsAttachedToVisualTree() && root.DataContext is IEntryForm form)
        {
            var subscription = new Subscription(root, form);
            form.EntrySubmitted += subscription.OnSubmitted;
            Subscriptions.Add(root, subscription);
        }
    }

    private static void Unsubscribe(Control root)
    {
        if (Subscriptions.TryGetValue(root, out var subscription))
        {
            subscription.Form.EntrySubmitted -= subscription.OnSubmitted;
            Subscriptions.Remove(root);
        }
    }

    private static void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.Handled || e.KeyModifiers != KeyModifiers.None
            || sender is not Control root || e.Source is not Visual source || !Submits(root, source))
        {
            return;
        }

        SuggestionBox.CloseList(source);
        var command = GetSubmitCommand(root);
        if (command?.CanExecute(null) == true)
        {
            command.Execute(null);
            e.Handled = true;
        }
    }

    // Walks from the focused control up to the dock: each step may claim Enter for itself.
    private static bool Submits(Control root, Visual source)
    {
        foreach (var visual in source.GetSelfAndVisualAncestors())
        {
            if (ReferenceEquals(visual, root))
            {
                return true;
            }

            switch (visual)
            {
                case AutoCompleteBox box when SuggestionBox.ClaimsEnter(box):
                case ComboBox { IsDropDownOpen: true }:
                case TextBox { AcceptsReturn: true }:
                case Button:
                    return false;
                case Control panel when Overlay.GetCancelCommand(panel) is not null:
                    return false;
            }
        }

        return false;
    }

    // Posted, like Overlay's focus: the add has just rebuilt rows and reset fields, and a
    // synchronous Focus() on a control not laid out yet is dropped. An overlay the submit opened
    // (the Leitstelle reminder) posted its own focus first and keeps it.
    private static void Refocus(Control root, bool added) =>
        Dispatcher.UIThread.Post(
            () =>
            {
                var focusManager = TopLevel.GetTopLevel(root)?.FocusManager;
                if (focusManager is null || !root.IsEffectivelyVisible || !root.IsEffectivelyEnabled
                    || (focusManager.GetFocusedElement() is Visual focused && Overlay.IsInOverlay(focused)))
                {
                    return;
                }

                var target = added ? FirstField(root) : FirstInvalidField(root);
                if (target is null)
                {
                    return; // refused without a marked field, as a duplicate Funkrufname is: stay put
                }

                target.Focus(NavigationMethod.Tab);
                SelectText(target);
            },
            DispatcherPriority.Background);

    private static InputElement? FirstField(Control root) =>
        GetFirstField(root) is { } first && CanTakeFocus(first) ? first : FirstTabStop(root);

    private static InputElement? FirstInvalidField(Control root) =>
        root.GetVisualDescendants().OfType<HeaderedContentControl>()
            .Where(field => field.Classes.Contains("field") && field.Classes.Contains("invalid") && field.IsEffectivelyVisible)
            .Select(FirstTabStop)
            .FirstOrDefault(target => target is not null);

    private static InputElement? FirstTabStop(Visual root) =>
        root.GetVisualDescendants().OfType<InputElement>()
            .FirstOrDefault(e => CanTakeFocus(e) && KeyboardNavigation.GetIsTabStop(e));

    // A picker with nothing left to pick (Kräfte FAHRZEUG once every vehicle is taken, or with
    // none in the Stammdaten) is no place to start an entry.
    private static bool CanTakeFocus(InputElement element) =>
        element.Focusable && element.IsEffectivelyVisible && element.IsEffectivelyEnabled
        && element is not ItemsControl { ItemCount: 0 };

    // Typing replaces what is there: an emptied field has nothing to select, a sticky or invalid one does.
    private static void SelectText(InputElement target)
    {
        var box = target as TextBox ?? (target as AutoCompleteBox)?.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
        box?.SelectAll();
    }

    private sealed class Subscription(Control root, IEntryForm form)
    {
        public IEntryForm Form { get; } = form;

        public void OnSubmitted(object? sender, EntrySubmittedEventArgs e) => Refocus(root, e.Added);
    }
}
