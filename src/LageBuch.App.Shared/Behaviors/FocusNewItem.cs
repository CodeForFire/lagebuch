using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.AppLogic.ViewModels;

namespace LageBuch.App.Shared.Behaviors;

/// <summary>
/// Puts the caret in the first field of a row "+ HINZUFÜGEN" just added (#543), set on the
/// <see cref="ItemsControl"/> that lists a Stammdaten section's rows. Before this, focus stayed on
/// the button, and reaching the new row took a Tab through every row above it.
///
/// It listens to <see cref="EditorSection.RowAdded"/> on the list's DataContext rather than to the
/// collection: an import or a discard rebuilds rows as well, and must not pull focus anywhere.
/// </summary>
public static class FocusNewItem
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<ItemsControl, bool>("IsEnabled", typeof(FocusNewItem));

    // The section a list listens to while it is on screen, held only while attached so a
    // section kept by the editor does not keep a view that is gone alive.
    private static readonly ConditionalWeakTable<ItemsControl, Subscription> Subscriptions = new();

    static FocusNewItem()
    {
        IsEnabledProperty.Changed.AddClassHandler<ItemsControl>((list, e) =>
        {
            list.AttachedToVisualTree -= OnAttachmentChanged;
            list.DetachedFromVisualTree -= OnAttachmentChanged;
            list.DataContextChanged -= OnDataContextChanged;
            Unsubscribe(list);
            if (e.NewValue is true)
            {
                list.AttachedToVisualTree += OnAttachmentChanged;
                list.DetachedFromVisualTree += OnAttachmentChanged;
                list.DataContextChanged += OnDataContextChanged;
                Subscribe(list);
            }
        });
    }

    public static void SetIsEnabled(ItemsControl target, bool value)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetValue(IsEnabledProperty, value);
    }

    public static bool GetIsEnabled(ItemsControl target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.GetValue(IsEnabledProperty);
    }

    private static void OnAttachmentChanged(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is ItemsControl list)
        {
            Subscribe(list);
        }
    }

    private static void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (sender is ItemsControl list)
        {
            Subscribe(list);
        }
    }

    private static void Subscribe(ItemsControl list)
    {
        Unsubscribe(list);
        if (list.IsAttachedToVisualTree() && list.DataContext is EditorSection section)
        {
            var subscription = new Subscription(list, section);
            section.RowAdded += subscription.OnRowAdded;
            Subscriptions.Add(list, subscription);
        }
    }

    private static void Unsubscribe(ItemsControl list)
    {
        if (Subscriptions.TryGetValue(list, out var subscription))
        {
            subscription.Section.RowAdded -= subscription.OnRowAdded;
            Subscriptions.Remove(list);
        }
    }

    // Posted: the row's container is only realized on the next layout pass, and a Focus() on a
    // control not laid out yet is dropped.
    private static void FocusFirstField(ItemsControl list, object row) =>
        Dispatcher.UIThread.Post(
            () =>
            {
                var field = list.ContainerFromItem(row)?.GetVisualDescendants().OfType<Control>()
                    .FirstOrDefault(e => e is TextBox or AutoCompleteBox && e.Focusable && e.IsEffectivelyVisible && e.IsEffectivelyEnabled);
                if (field is null)
                {
                    return;
                }

                field.Focus(NavigationMethod.Tab);
                field.BringIntoView();
            },
            DispatcherPriority.Background);

    private sealed class Subscription(ItemsControl list, EditorSection section)
    {
        public EditorSection Section { get; } = section;

        public void OnRowAdded(object? sender, RowAddedEventArgs e) => FocusFirstField(list, e.Row);
    }
}
