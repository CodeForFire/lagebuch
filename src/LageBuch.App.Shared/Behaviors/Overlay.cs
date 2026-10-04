using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace LageBuch.App.Shared.Behaviors;

/// <summary>
/// The keyboard contract every overlay keeps (#538, contract 1 of #545), set on the overlay's
/// root: focus goes inside once it is shown, Tab cycles inside it, Esc runs
/// <see cref="CancelCommandProperty"/>, Enter runs <see cref="PrimaryCommandProperty"/> (never
/// inside multi-line text), and focus goes back to the control that opened it.
///
/// A destructive confirm sets no primary command: Enter then activates the focused button, which
/// starts out as ABBRECHEN, so a reflex Enter cancels.
///
/// An inline panel (<see cref="IsInlineProperty"/>) is drawn by its module's own XAML and shown
/// by <c>IsVisible</c>, so it opens and closes with that rather than with the visual tree, and it
/// is not modal: the page beside it stays live, so focus that leaves it is not pulled back.
/// </summary>
public static class Overlay
{
    public static readonly AttachedProperty<ICommand?> CancelCommandProperty =
        AvaloniaProperty.RegisterAttached<Control, ICommand?>("CancelCommand", typeof(Overlay));

    public static readonly AttachedProperty<ICommand?> PrimaryCommandProperty =
        AvaloniaProperty.RegisterAttached<Control, ICommand?>("PrimaryCommand", typeof(Overlay));

    /// <summary>Where focus goes when the overlay opens; the first tab stop when unset.</summary>
    public static readonly AttachedProperty<Control?> InitialFocusProperty =
        AvaloniaProperty.RegisterAttached<Control, Control?>("InitialFocus", typeof(Overlay));

    /// <summary>
    /// Marks an inline panel: it opens and closes with its own <c>IsVisible</c>, and focus may
    /// leave it for the page beside it.
    /// </summary>
    public static readonly AttachedProperty<bool> IsInlineProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsInline", typeof(Overlay));

    /// <summary>
    /// Where focus goes on close when the opener is gone, as a row button is once saving rebuilt
    /// its row.
    /// </summary>
    public static readonly AttachedProperty<Control?> FallbackFocusProperty =
        AvaloniaProperty.RegisterAttached<Control, Control?>("FallbackFocus", typeof(Overlay));

    // The control that had focus when the overlay was shown: the one focus goes back to.
    private static readonly ConditionalWeakTable<Control, IInputElement> Openers = new();

    static Overlay()
    {
        CancelCommandProperty.Changed.AddClassHandler<Control>((root, e) =>
        {
            Detach(root);
            if (e.NewValue is ICommand)
            {
                Attach(root);
            }
        });
    }

    public static void SetCancelCommand(Control target, ICommand? value)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetValue(CancelCommandProperty, value);
    }

    public static ICommand? GetCancelCommand(Control target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.GetValue(CancelCommandProperty);
    }

    public static void SetPrimaryCommand(Control target, ICommand? value)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetValue(PrimaryCommandProperty, value);
    }

    public static ICommand? GetPrimaryCommand(Control target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.GetValue(PrimaryCommandProperty);
    }

    public static void SetInitialFocus(Control target, Control? value)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetValue(InitialFocusProperty, value);
    }

    public static Control? GetInitialFocus(Control target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.GetValue(InitialFocusProperty);
    }

    public static void SetIsInline(Control target, bool value)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetValue(IsInlineProperty, value);
    }

    public static bool GetIsInline(Control target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.GetValue(IsInlineProperty);
    }

    public static void SetFallbackFocus(Control target, Control? value)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetValue(FallbackFocusProperty, value);
    }

    public static Control? GetFallbackFocus(Control target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.GetValue(FallbackFocusProperty);
    }

    private static void Attach(Control root)
    {
        // Focusable so focus can rest on the overlay itself when the focused control inside is
        // disabled (a busy form), but no tab stop of its own.
        root.Focusable = true;
        KeyboardNavigation.SetIsTabStop(root, false);
        KeyboardNavigation.SetTabNavigation(root, KeyboardNavigationMode.Cycle);
        root.AttachedToVisualTree += OnAttached;
        root.DetachedFromVisualTree += OnDetached;
        root.AddHandler(InputElement.KeyDownEvent, OnKeyDown);
        root.AddHandler(InputElement.LostFocusEvent, OnLostFocus);
        root.PropertyChanged += OnPropertyChanged;
        if (root.IsAttachedToVisualTree() && !GetIsInline(root))
        {
            Opened(root);
        }
    }

    private static void Detach(Control root)
    {
        root.AttachedToVisualTree -= OnAttached;
        root.DetachedFromVisualTree -= OnDetached;
        root.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        root.RemoveHandler(InputElement.LostFocusEvent, OnLostFocus);
        root.PropertyChanged -= OnPropertyChanged;
    }

    // An inline panel stays in the tree; arriving at its module by the rail is not opening it, so
    // only its IsVisible counts.
    private static void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Control root && !GetIsInline(root))
        {
            Opened(root);
        }
    }

    private static void Opened(Control root)
    {
        Openers.Remove(root);
        if (TopLevel.GetTopLevel(root)?.FocusManager?.GetFocusedElement() is { } opener && !IsWithin(root, opener))
        {
            Openers.Add(root, opener);
        }

        // Posted, at a lower priority than a view's own posted focus: the overlay is not laid out
        // yet at attach time, so a synchronous Focus() is dropped, and a view that places focus
        // itself (OperatorPrompt, by stage) keeps that placement.
        Dispatcher.UIThread.Post(() => FocusInitial(root), DispatcherPriority.Background);
    }

    private static void FocusInitial(Control root)
    {
        var focusManager = TopLevel.GetTopLevel(root)?.FocusManager;
        if (focusManager is null || IsWithin(root, focusManager.GetFocusedElement()))
        {
            return;
        }

        var target = GetInitialFocus(root) ?? FirstTabStop(root);
        if (target is null)
        {
            root.Focus();
            return;
        }

        target.Focus(NavigationMethod.Tab);
        if (target is TextBox box)
        {
            box.SelectAll();
        }
    }

    private static InputElement? FirstTabStop(Control root) =>
        root.GetVisualDescendants().OfType<InputElement>()
            .FirstOrDefault(e => e.Focusable && e.IsEffectivelyVisible && e.IsEffectivelyEnabled && KeyboardNavigation.GetIsTabStop(e));

    private static void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not Control root)
        {
            return;
        }

        if (GetIsInline(root))
        {
            // Leaving the module by the rail is not closing the panel; nothing to give back.
            Openers.Remove(root);
            return;
        }

        if (Openers.TryGetValue(root, out var opener))
        {
            Closed(root, TopLevel.GetTopLevel(e.RootVisual), opener);
        }
    }

    private static void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Visual.IsVisibleProperty || sender is not Control root
            || !GetIsInline(root) || !root.IsAttachedToVisualTree())
        {
            return;
        }

        if (root.IsVisible)
        {
            Opened(root);
            return;
        }

        // Closed. Focus goes back only if it was in the panel: a Lagebuchführer who has already
        // moved on to the page beside it keeps their place.
        var topLevel = TopLevel.GetTopLevel(root);
        var focused = topLevel?.FocusManager?.GetFocusedElement();
        Openers.TryGetValue(root, out var opener);
        if (focused is null || IsWithin(root, focused))
        {
            Closed(root, topLevel, opener);
        }
        else
        {
            Openers.Remove(root);
        }
    }

    private static void Closed(Control root, TopLevel? topLevel, IInputElement? opener)
    {
        Openers.Remove(root);
        var fallback = GetFallbackFocus(root);
        Dispatcher.UIThread.Post(() =>
        {
            // Another overlay may have opened in the same breath (a confirm chaining into the PDF
            // options); focus stays with it.
            if (topLevel?.FocusManager?.GetFocusedElement() is Visual focused && IsInOverlay(focused))
            {
                return;
            }

            var target = CanTakeFocus(opener) ? opener : CanTakeFocus(fallback) ? fallback : null;
            target?.Focus(NavigationMethod.Tab);
        });
    }

    private static bool CanTakeFocus([NotNullWhen(true)] IInputElement? element) =>
        element is Visual visual && visual.IsAttachedToVisualTree()
        && element.IsEffectivelyVisible && element.IsEffectivelyEnabled;

    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || sender is not Control root)
        {
            return;
        }

        var command = e.Key switch
        {
            Key.Escape => GetCancelCommand(root),
            Key.Enter when e.Source is not TextBox { AcceptsReturn: true } => GetPrimaryCommand(root),
            _ => null,
        };
        if (command?.CanExecute(null) == true)
        {
            command.Execute(null);
            e.Handled = true;
        }
    }

    // An overlay is modal, yet disabling the focused control (a busy form) moves focus on to the
    // next control, which is on the page behind; the next key would land there. Keep it on the
    // overlay instead.
    private static void OnLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control root || GetIsInline(root))
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (root.IsAttachedToVisualTree()
                && !IsWithin(root, TopLevel.GetTopLevel(root)?.FocusManager?.GetFocusedElement()))
            {
                root.Focus();
            }
        });
    }

    // Logical as well as visual: a drop-down's items live in a popup, outside the overlay's visual
    // tree but under it logically.
    private static bool IsWithin(Control root, IInputElement? element) =>
        element is Visual visual
        && (ReferenceEquals(visual, root) || root.IsVisualAncestorOf(visual)
            || root.IsLogicalAncestorOf(visual));

    internal static bool IsInOverlay(Visual visual) =>
        visual.GetSelfAndVisualAncestors().OfType<Control>().Any(c => GetCancelCommand(c) is not null);
}
