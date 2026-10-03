using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace LageBuch.Acceptance.Tests;

// #537: one set of keyboard helpers for every headless test, so a keyboard flow reads as the
// keys a Lagebuchführer presses. Each input helper pumps the dispatcher afterwards, the way every
// test used to do by hand after each KeyPressQwerty.
internal static class KeyboardInput
{
    public static void Tab(this Window window) => window.Press(PhysicalKey.Tab);

    public static void ShiftTab(this Window window) => window.Press(PhysicalKey.Tab, RawInputModifiers.Shift);

    // Press and release, as a real key does: a Button activates on Space only at KeyUp.
    public static void Press(this Window window, PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.KeyPressQwerty(key, modifiers);
        window.KeyReleaseQwerty(key, modifiers);
        Dispatcher.UIThread.RunJobs();
    }

    public static void Type(this Window window, string text)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.KeyTextInput(text);
        Dispatcher.UIThread.RunJobs();
    }

    public static IInputElement? FocusedElement(this Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return window.FocusManager?.GetFocusedElement();
    }

    // The focused element is the control itself, or a part of its own template: AutoCompleteBox
    // hands focus to the TextBox inside it (see OperatorPromptFocusTests).
    public static bool IsFocused(this Window window, Control expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        for (var current = window.FocusedElement() as StyledElement; current is not null; current = current.TemplatedParent as StyledElement)
        {
            if (ReferenceEquals(current, expected))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsFocusWithin(this Window window, Control container)
    {
        ArgumentNullException.ThrowIfNull(container);
        return window.FocusedElement() is Visual focused
            && (ReferenceEquals(focused, container) || container.IsVisualAncestorOf(focused));
    }

    public static void AssertFocused(this Window window, Control expected) =>
        Assert.True(window.IsFocused(expected), $"Expected focus on {Describe(expected)}, but it is on {window.DescribeFocus()}.");

    public static void AssertFocusWithin(this Window window, Control container) =>
        Assert.True(window.IsFocusWithin(container), $"Expected focus inside {Describe(container)}, but it is on {window.DescribeFocus()}.");

    public static string DescribeFocus(this Window window) => window.FocusedElement() switch
    {
        null => "nothing",
        Control control => Describe(control),
        var other => other.GetType().Name,
    };

    public static string Describe(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return string.IsNullOrEmpty(control.Name) ? control.GetType().Name : $"{control.GetType().Name} '{control.Name}'";
    }
}
