using Avalonia.Controls;
using Avalonia.Input;

namespace LageBuch.Acceptance.Tests;

// #545's keystroke budget: the KeyboardInput helpers, counted. One press is one key or one chord
// (Strg+N counts once, the way a hand experiences it), and typed text counts a press per character.
// TabTo walks focus with real Tab presses only, so a flow states its goal and the count states what
// reaching it cost.
internal sealed class CountingKeyboard(Window window)
{
    // Further than any form in the app; a flow that needs more has lost its way.
    private const int MaxTabs = 60;

    public int Presses { get; private set; }

    // The part of Presses that was text. It is the same in every version, so the keys spent
    // getting somewhere are Presses - Typed.
    public int Typed { get; private set; }

    public int Navigation => Presses - Typed;

    public void Tab()
    {
        window.Tab();
        Presses++;
    }

    public void Press(PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.Press(key, modifiers);
        Presses++;
    }

    public void Type(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        window.Type(text);
        Presses += text.Length;
        Typed += text.Length;
    }

    public void TabTo(Control target) => TabTo(KeyboardInput.Describe(target), () => window.IsFocused(target));

    public void TabTo(string what, Func<bool> reached)
    {
        ArgumentNullException.ThrowIfNull(reached);
        for (var i = 0; !reached(); i++)
        {
            Assert.True(i < MaxTabs, $"Tab never reached {what}; focus is on {window.DescribeFocus()}.");
            Tab();
        }
    }
}
