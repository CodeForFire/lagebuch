using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.AppLogic.ViewModels;

namespace LageBuch.App.Shared.Behaviors;

/// <summary>
/// Keeps focus where the user left it in the Einsatz workspace (#542). Set <see cref="RailProperty"/>
/// on the workspace's root to the module rail.
///
/// A module takes focus only when the user asked for it: a warning bar jumped to it
/// (<see cref="IncidentWorkspaceViewModel.ModuleFocusRequested"/>) or its rail tab was clicked.
/// Arrowing the rail opens each module without leaving the rail, which a view that focused itself
/// on arrival made impossible: the rail builds a new view on every switch.
///
/// Focus that a rebuild (a read-only flip, possibly synced from another device) or a lost connection
/// took away comes back: to the same control when it is still there, to the module's first field
/// when the module was rebuilt, and to the open rail tab when neither can take it.
/// </summary>
public static class WorkspaceFocus
{
    /// <summary>The module rail. Its DataContext is the workspace view model.</summary>
    public static readonly AttachedProperty<TabControl?> RailProperty =
        AvaloniaProperty.RegisterAttached<Control, TabControl?>("Rail", typeof(WorkspaceFocus));

    /// <summary>
    /// Marks where a module without an entry dock starts, e.g. the Kontakte search. A module with
    /// a dock starts at its <see cref="EntryForm.FirstFieldProperty"/>.
    /// </summary>
    public static readonly AttachedProperty<bool> IsStartProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsStart", typeof(WorkspaceFocus));

    private static readonly ConditionalWeakTable<Control, Subscription> Subscriptions = new();

    static WorkspaceFocus()
    {
        RailProperty.Changed.AddClassHandler<Control>((root, e) =>
        {
            Detach(root);
            if (e.NewValue is TabControl)
            {
                Attach(root);
            }
        });
    }

    public static void SetRail(Control target, TabControl? value)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetValue(RailProperty, value);
    }

    public static TabControl? GetRail(Control target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.GetValue(RailProperty);
    }

    public static void SetIsStart(Control target, bool value)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetValue(IsStartProperty, value);
    }

    public static bool GetIsStart(Control target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.GetValue(IsStartProperty);
    }

    private static void Attach(Control root)
    {
        root.AddHandler(InputElement.GotFocusEvent, OnGotFocus, RoutingStrategies.Bubble, handledEventsToo: true);
        root.AddHandler(InputElement.TappedEvent, OnTapped, RoutingStrategies.Bubble, handledEventsToo: true);
        root.AttachedToVisualTree += OnAttached;
        root.DetachedFromVisualTree += OnDetached;
        root.DataContextChanged += OnDataContextChanged;
        Subscribe(root);
    }

    private static void Detach(Control root)
    {
        root.RemoveHandler(InputElement.GotFocusEvent, OnGotFocus);
        root.RemoveHandler(InputElement.TappedEvent, OnTapped);
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

    // Held only while attached, so the long-lived view model does not keep a closed view alive.
    private static void Subscribe(Control root)
    {
        Unsubscribe(root);
        if (root.IsAttachedToVisualTree() && GetRail(root) is { } rail && root.DataContext is IncidentWorkspaceViewModel workspace)
        {
            var subscription = new Subscription(rail, workspace);
            workspace.ModuleFocusRequested += subscription.OnModuleFocusRequested;
            workspace.PropertyChanged += subscription.OnWorkspaceChanged;
            workspace.NavItems.CollectionChanged += subscription.OnNavItemsChanged;
            Subscriptions.Add(root, subscription);
        }
    }

    private static void Unsubscribe(Control root)
    {
        if (Subscriptions.TryGetValue(root, out var subscription))
        {
            subscription.Workspace.ModuleFocusRequested -= subscription.OnModuleFocusRequested;
            subscription.Workspace.PropertyChanged -= subscription.OnWorkspaceChanged;
            subscription.Workspace.NavItems.CollectionChanged -= subscription.OnNavItemsChanged;
            Subscriptions.Remove(root);
        }
    }

    private static void OnGotFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is Control root && Subscriptions.TryGetValue(root, out var subscription)
            && e.Source is InputElement focused && !Overlay.IsInOverlay(focused))
        {
            subscription.Remember(focused);
        }
    }

    // A click on a rail tab asks for that module; ↑/↓ on the rail does not. Tapped comes after
    // the press that selected the tab, so the new module is already the selected one.
    private static void OnTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control root && Subscriptions.TryGetValue(root, out var subscription)
            && e.Source is Visual source && subscription.RailTabOf(source) is not null)
        {
            subscription.FocusStart();
        }
    }

    private sealed class Subscription(TabControl rail, IncidentWorkspaceViewModel workspace)
    {
        private WeakReference<InputElement>? _remembered;
        private Place _rememberedPlace;

        private enum Place
        {
            Elsewhere,
            Rail,
            Module,
        }

        public IncidentWorkspaceViewModel Workspace { get; } = workspace;

        public void Remember(InputElement focused)
        {
            _remembered = new WeakReference<InputElement>(focused);
            _rememberedPlace = RailTabOf(focused) is not null ? Place.Rail
                : rail.IsVisualAncestorOf(focused) ? Place.Module
                : Place.Elsewhere;
        }

        public TabItem? RailTabOf(Visual visual) =>
            visual.GetSelfAndVisualAncestors().OfType<TabItem>()
                .FirstOrDefault(tab => rail.IndexFromContainer(tab) >= 0);

        public void OnModuleFocusRequested(object? sender, ModuleFocusRequestedEventArgs e) => FocusStart(e.ToStartField);

        // Reconnected: the module area is enabled again.
        public void OnWorkspaceChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (string.Equals(e.PropertyName, nameof(IncidentWorkspaceViewModel.IsInputEnabled), StringComparison.Ordinal)
                && Workspace.IsInputEnabled)
            {
                Restore();
            }
        }

        // Rebuilt: every rail tab and module view is new.
        public void OnNavItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                Restore();
            }
        }

        // ContextIdle: after the new module is laid out, and after the Background-priority focus a
        // module places itself (a warning bar's Druck field, #539), which is left where it is.
        // toStartField (Ctrl+N, #544) moves the caret even from elsewhere in the module, but never
        // out of an overlay.
        public void FocusStart(bool toStartField = false) =>
            Dispatcher.UIThread.Post(
                () =>
                {
                    if (FocusManager() is not { } focusManager || ModuleContent() is not { } content)
                    {
                        return;
                    }

                    if (focusManager.GetFocusedElement() is Visual focused
                        && ((!toStartField && content.IsVisualAncestorOf(focused)) || Overlay.IsInOverlay(focused)))
                    {
                        return;
                    }

                    if (StartField(content) is { } start)
                    {
                        start.Focus(NavigationMethod.Tab);
                        EntryForm.SelectText(start);
                    }
                },
                DispatcherPriority.ContextIdle);

        // Only focus that was lost is put back: whatever the user moved to since stays.
        private void Restore() =>
            Dispatcher.UIThread.Post(
                () =>
                {
                    if (FocusManager() is not { } focusManager || !IsLost(focusManager.GetFocusedElement()))
                    {
                        return;
                    }

                    var remembered = _remembered is not null && _remembered.TryGetTarget(out var element) ? element : null;
                    InputElement? target = remembered is not null && remembered.IsAttachedToVisualTree() && EntryForm.CanTakeFocus(remembered)
                        ? remembered
                        : _rememberedPlace switch
                        {
                            Place.Module => (ModuleContent() is { } content ? StartField(content) : null) ?? SelectedRailTab(),
                            Place.Rail => SelectedRailTab(),
                            _ => null,
                        };
                    target?.Focus(NavigationMethod.Tab);
                },
                DispatcherPriority.ContextIdle);

        private static bool IsLost(IInputElement? focused) =>
            focused is null or TopLevel || (focused is Visual visual && !visual.IsAttachedToVisualTree());

        private IFocusManager? FocusManager() =>
            rail.IsAttachedToVisualTree() ? TopLevel.GetTopLevel(rail)?.FocusManager : null;

        private Control? ModuleContent() =>
            rail.GetVisualDescendants().OfType<ContentPresenter>()
                .FirstOrDefault(p => string.Equals(p.Name, "PART_SelectedContentHost", StringComparison.Ordinal))?.Child;

        private TabItem? SelectedRailTab() =>
            rail.SelectedItem is { } item && rail.ContainerFromItem(item) is TabItem { } tab && EntryForm.CanTakeFocus(tab) ? tab : null;

        // The module's own mark, else its entry dock's first field. A read-only module hides its
        // dock and has no start.
        private static InputElement? StartField(Control content)
        {
            var controls = content.GetVisualDescendants().OfType<Control>().ToList();
            return controls.FirstOrDefault(c => GetIsStart(c) && EntryForm.CanTakeFocus(c))
                ?? controls
                    .Where(c => EntryForm.GetSubmitCommand(c) is not null && c.IsEffectivelyVisible && c.IsEffectivelyEnabled)
                    .Select(EntryForm.FirstField)
                    .FirstOrDefault(field => field is not null);
        }
    }
}
