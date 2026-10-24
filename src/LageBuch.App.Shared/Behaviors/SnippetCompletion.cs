using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using LageBuch.AppLogic.Services;
using LageBuch.Persistence.MasterData;

namespace LageBuch.App.Shared.Behaviors;

/// <summary>
/// <c>::kürzel</c> in a text field: typing <c>::</c> lists the Textbausteine, the letters after it
/// narrow the list, and taking one replaces <c>::kürzel</c> with its text and selects its first
/// <c>[…]</c> gap. Set <see cref="SnippetsProperty"/> on a <see cref="TextBox"/> to switch it on;
/// an empty list leaves the field exactly as it was.
///
/// <list type="bullet">
/// <item>The request is read from the text and the caret after every change, never from key
/// presses, so an Android soft keyboard -- which sends no KeyDown for letters -- triggers it too.
/// The rules are <see cref="TextSnippetMatcher"/>'s.</item>
/// <item>While the list is open, ↑/↓ choose, Enter and Tab insert, Esc closes the list and nothing
/// else. These keys are taken in a tunnel handler on the field and marked handled, which keeps them
/// from an overlay's Enter/Esc and an edit panel's Enter binding. The dock's own tunnel handler sits
/// above the field and sees Enter first, so it asks <see cref="ClaimsEnter"/>.</item>
/// <item>The list opens above the field: every composer sits at the bottom of its view, and on a
/// phone the soft keyboard takes the space below.</item>
/// <item>A tap on a row inserts it. Gap selection is skipped on Android, where an IME and a
/// programmatic selection fight (SuggestionBox precedent); the caret follows the text there.</item>
/// </list>
/// </summary>
public static class SnippetCompletion
{
    public static readonly AttachedProperty<IReadOnlyList<TextSnippet>?> SnippetsProperty =
        AvaloniaProperty.RegisterAttached<TextBox, IReadOnlyList<TextSnippet>?>("Snippets", typeof(SnippetCompletion));

    private static readonly ConditionalWeakTable<TextBox, Session> Sessions = new();

    static SnippetCompletion()
    {
        SnippetsProperty.Changed.AddClassHandler<TextBox>((box, e) =>
        {
            box.TextChanged -= OnTextChanged;
            box.RemoveHandler(InputElement.KeyDownEvent, OnPreviewKeyDown);
            box.LostFocus -= OnLostFocus;
            box.DetachedFromVisualTree -= OnDetached;
            if (e.NewValue is IReadOnlyList<TextSnippet> { Count: > 0 })
            {
                box.TextChanged += OnTextChanged;
                box.AddHandler(InputElement.KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
                box.LostFocus += OnLostFocus;
                box.DetachedFromVisualTree += OnDetached;
            }
            else
            {
                Close(box);
            }
        });
    }

    public static void SetSnippets(TextBox target, IReadOnlyList<TextSnippet>? value)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetValue(SnippetsProperty, value);
    }

    public static IReadOnlyList<TextSnippet>? GetSnippets(TextBox target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.GetValue(SnippetsProperty);
    }

    /// <summary>
    /// Whether Enter belongs to the field: its Textbaustein list is open, so Enter inserts the
    /// chosen one and must not also submit the form it sits in.
    /// </summary>
    public static bool ClaimsEnter(TextBox box)
    {
        ArgumentNullException.ThrowIfNull(box);
        return Sessions.TryGetValue(box, out var session) && session.IsOpen;
    }

    /// <summary>The Textbausteine the open list offers, in order; empty while it is closed.</summary>
    public static IReadOnlyList<TextSnippet> OfferedSnippets(TextBox box)
    {
        ArgumentNullException.ThrowIfNull(box);
        return Sessions.TryGetValue(box, out var session) && session.IsOpen ? session.Matches : Array.Empty<TextSnippet>();
    }

    /// <summary>The index of the row Enter would insert, or -1 while the list is closed.</summary>
    public static int SelectedIndex(TextBox box)
    {
        ArgumentNullException.ThrowIfNull(box);
        return Sessions.TryGetValue(box, out var session) && session.IsOpen ? session.Selected : -1;
    }

    // Posted: Avalonia raises TextChanged before it moves the caret past the typed character, and
    // the request is defined by the text *before the caret*.
    private static void OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is TextBox box && !SessionOf(box).Applying)
        {
            Dispatcher.UIThread.Post(() => Refresh(box));
        }
    }

    private static void OnLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox box)
        {
            Close(box);
        }
    }

    private static void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is TextBox box)
        {
            Close(box);
        }
    }

    private static void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox box || !Sessions.TryGetValue(box, out var session) || !session.IsOpen
            || e.KeyModifiers != KeyModifiers.None)
        {
            // Left/Right/Home/End move the caret off or onto a request without changing the text.
            if (sender is TextBox moved && e.Key is Key.Left or Key.Right or Key.Home or Key.End)
            {
                Dispatcher.UIThread.Post(() => Refresh(moved));
            }

            return;
        }

        switch (e.Key)
        {
            case Key.Down:
                session.Select(Math.Min(session.Selected + 1, session.Matches.Count - 1));
                break;
            case Key.Up:
                session.Select(Math.Max(session.Selected - 1, 0));
                break;
            case Key.Enter:
            case Key.Tab:
                Insert(box, session, session.Matches[session.Selected]);
                break;
            case Key.Escape:
                Close(box);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private static void Refresh(TextBox box)
    {
        var snippets = GetSnippets(box);
        if (snippets is not { Count: > 0 } || !box.IsFocused
            || !TextSnippetMatcher.TryGetQuery(box.Text, box.CaretIndex, out var start, out var query))
        {
            Close(box);
            return;
        }

        var matches = TextSnippetMatcher.Match(snippets, query);
        if (matches.Count == 0)
        {
            Close(box);
            return;
        }

        SessionOf(box).Show(start, matches);
    }

    private static void Insert(TextBox box, Session session, TextSnippet snippet)
    {
        var text = box.Text ?? string.Empty;
        var caret = Math.Clamp(box.CaretIndex, session.Start, text.Length);
        var result = TextSnippetMatcher.Apply(text, session.Start, caret, snippet, selectGap: !OperatingSystem.IsAndroid());
        session.Hide();

        session.Applying = true;
        try
        {
            box.SetCurrentValue(TextBox.TextProperty, result.Text);
            box.CaretIndex = result.SelectionStart + result.SelectionLength;
            box.SelectionStart = result.SelectionStart;
            box.SelectionEnd = result.SelectionStart + result.SelectionLength;
        }
        finally
        {
            session.Applying = false;
        }

        box.Focus();
    }

    private static void Close(TextBox box)
    {
        if (Sessions.TryGetValue(box, out var session))
        {
            session.Hide();
        }
    }

    private static Session SessionOf(TextBox box) => Sessions.GetValue(box, b => new Session(b));

    /// <summary>One field's open list: where its request starts, what it offers, which row is chosen.</summary>
    private sealed class Session
    {
        private readonly TextBox _box;
        private readonly StackPanel _rows = new();
        private Popup? _popup;

        public Session(TextBox box) => _box = box;

        public bool Applying { get; set; }

        public bool IsOpen => _popup?.IsOpen == true;

        public int Start { get; private set; }

        public IReadOnlyList<TextSnippet> Matches { get; private set; } = Array.Empty<TextSnippet>();

        public int Selected { get; private set; }

        public void Show(int start, IReadOnlyList<TextSnippet> matches)
        {
            Start = start;
            Matches = matches;
            Selected = 0;
            Build();
            var popup = _popup ??= CreatePopup();
            if (!popup.IsOpen)
            {
                popup.IsOpen = true;
            }
        }

        public void Hide()
        {
            if (_popup is { IsOpen: true } popup)
            {
                popup.IsOpen = false;
            }

            Matches = Array.Empty<TextSnippet>();
        }

        public void Select(int index)
        {
            Selected = index;
            for (var i = 0; i < _rows.Children.Count; i++)
            {
                ((Border)_rows.Children[i]).Background = i == index ? Brush("SurfaceTopBrush") : Brushes.Transparent;
            }
        }

        private Popup CreatePopup()
        {
            var border = new Border
            {
                Background = Brush("SurfaceRaisedBrush"),
                BorderBrush = Brush("BorderStrongBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(4),
                MinWidth = 320,
                MaxWidth = 640,
                Child = OperatingSystem.IsAndroid()
                    ? _rows
                    : new StackPanel
                    {
                        Children =
                        {
                            _rows,

                            // A phone has no arrow keys to explain; a desktop list without the hint
                            // leaves the Funker guessing whether Enter inserts or sends.
                            new TextBlock
                            {
                                Text = "↑↓ wählen · Enter einfügen · Esc schließen",
                                FontSize = 11,
                                Foreground = Brush("TextSecondaryBrush"),
                                Margin = new Thickness(8, 4, 8, 2),
                            },
                        },
                    },
            };
            AutomationProperties.SetName(_rows, "Textbausteine");
            var popup = new Popup
            {
                PlacementTarget = _box,
                Placement = PlacementMode.TopEdgeAlignedLeft,
                VerticalOffset = -4,
                IsLightDismissEnabled = false,

                // Drawn in the window's own overlay layer, as every popup is on Android: the list
                // belongs to one field and never needs to leave the window, and a separate native
                // popup window is one more thing that can take activation away from the field.
                ShouldUseOverlayLayer = true,
                Child = border,
            };

            // A popup opens only from inside the logical tree; one made in code has to be given a
            // parent, the way Avalonia's own ToolTip parents its popup to the control it describes.
            ((ISetLogicalParent)popup).SetParent(_box);
            return popup;
        }

        private void Build()
        {
            _rows.Children.Clear();
            foreach (var snippet in Matches)
            {
                var row = new Border
                {
                    Padding = new Thickness(8, 5),
                    CornerRadius = new CornerRadius(4),
                    Cursor = new Cursor(StandardCursorType.Hand),
                    Child = RowContent(snippet),
                };
                AutomationProperties.SetName(row, $"{TextSnippetMatcher.Trigger}{snippet.Shortcut} {snippet.Text}");

                // PointerPressed, not Tapped: the press must not move focus out of the field first,
                // or LostFocus would close the list before the row could be taken.
                row.PointerPressed += (_, e) =>
                {
                    e.Handled = true;
                    Insert(_box, this, snippet);
                };
                _rows.Children.Add(row);
            }

            Select(0);
        }

        private StackPanel RowContent(TextSnippet snippet) => new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children =
            {
                new TextBlock
                {
                    Text = TextSnippetMatcher.Trigger + snippet.Shortcut,
                    FontWeight = FontWeight.SemiBold,
                    MinWidth = 56,
                },
                new TextBlock
                {
                    Text = snippet.Category,
                    Foreground = Brush("TextSecondaryBrush"),
                    FontSize = 12,
                    MinWidth = 90,
                    VerticalAlignment = VerticalAlignment.Center,
                },
                new TextBlock
                {
                    Text = snippet.Text,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 420,
                },
            },
        };

        // The popup lives in its own root, outside the field's resource scope, so the theme's
        // brushes are looked up from the field and set on the popup's controls directly.
        private IBrush Brush(string key) =>
            _box.TryFindResource(key, _box.ActualThemeVariant, out var value) && value is IBrush brush ? brush : Brushes.Transparent;
    }
}
