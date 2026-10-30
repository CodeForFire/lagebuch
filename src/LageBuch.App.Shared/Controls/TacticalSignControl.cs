using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using LageBuch.Domain.ValueObjects;

namespace LageBuch.App.Shared.Controls;

/// <summary>
/// A unit's taktisches Zeichen after DV 102: the Kraftfahrzeug Grundzeichen in Feuerwehr red with
/// the Fahrzeugtyp inside, the Funkrufname to its right and the Stärke, then the Status, beneath
/// it (§1.6: "rechts daneben: Bezeichnung der Einheit", "darunter: Stärke"). Drawn rather than
/// loaded from SVG, after the CC0 set of jonas-koeritz/Taktische-Zeichen, so it needs no package
/// and draws the same on a phone. The sign's body sits at the top left, and from
/// <see cref="SymbolCenter"/> it draws the Verbindungsstrich back to the unit's pin, which stands
/// <see cref="LabelOffset"/> away (§1.5: a sign moved aside "mit Verbindungsstrichen zur jeweiligen
/// Örtlichkeit"). The strich leaves the control's bounds on purpose; only the sign takes the pointer.
/// </summary>
public sealed class TacticalSignControl : Control
{
    /// <summary>The Grundzeichen's width; its body is <see cref="BodyHeight"/> tall, the wheels hang below.</summary>
    public const double SymbolWidth = 48;

    public const double BodyHeight = 26;

    public static readonly StyledProperty<string?> VehicleTypeProperty =
        AvaloniaProperty.Register<TacticalSignControl, string?>(nameof(VehicleType));

    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<TacticalSignControl, string?>(nameof(Title));

    public static readonly StyledProperty<string?> StrengthProperty =
        AvaloniaProperty.Register<TacticalSignControl, string?>(nameof(Strength));

    public static readonly StyledProperty<string?> StatusProperty =
        AvaloniaProperty.Register<TacticalSignControl, string?>(nameof(Status));

    /// <summary>How far the sign stands from its pin; null draws no Verbindungsstrich.</summary>
    public static readonly StyledProperty<LabelOffset?> LabelOffsetProperty =
        AvaloniaProperty.Register<TacticalSignControl, LabelOffset?>(nameof(LabelOffset));

    private const double WheelRadius = 2.4;
    private const double SymbolHeight = BodyHeight + 7;
    private const double Gap = 4;

    // The body's top edge sags towards the middle, as on the DV 102 Kraftfahrzeug sign.
    private const double Sag = 3.5;

    private static readonly IBrush Red = new ImmutableSolidColorBrush(Color.FromRgb(0xFF, 0x00, 0x00));

    // Twice the edge's width: clipped to the body, only its inner half shows, just inside the outline.
    private static readonly IPen InnerEdge = new ImmutablePen(Brushes.White, 3.2);
    private static readonly IPen Outline = new ImmutablePen(Brushes.Black, 1);
    private static readonly IPen Wheel = new ImmutablePen(Brushes.Black, 1.2);

    // Black, as DV 102 draws lines; a white halo keeps it visible on a dark tile or a forest.
    private static readonly IPen Strich = new ImmutablePen(Brushes.Black, 1.5);
    private static readonly IPen StrichHalo = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)), 3.5);

    private static readonly IBrush TextBackground = new ImmutableSolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF));
    private static readonly IBrush TextInk = new ImmutableSolidColorBrush(Color.FromRgb(0x21, 0x21, 0x21));
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("de-DE");

    static TacticalSignControl()
    {
        AffectsMeasure<TacticalSignControl>(VehicleTypeProperty, TitleProperty, StrengthProperty, StatusProperty);
        AffectsRender<TacticalSignControl>(VehicleTypeProperty, TitleProperty, StrengthProperty, StatusProperty, LabelOffsetProperty);
    }

    /// <summary>The body's middle: where the sign is anchored to its offset from the pin.</summary>
    public static Point SymbolCenter { get; } = new(SymbolWidth / 2, BodyHeight / 2);

    /// <summary>The Kurzbezeichnung inside the sign; null or blank draws the bare Kraftfahrzeug sign.</summary>
    public string? VehicleType
    {
        get => GetValue(VehicleTypeProperty);
        set => SetValue(VehicleTypeProperty, value);
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Strength
    {
        get => GetValue(StrengthProperty);
        set => SetValue(StrengthProperty, value);
    }

    public string? Status
    {
        get => GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    public LabelOffset? LabelOffset
    {
        get => GetValue(LabelOffsetProperty);
        set => SetValue(LabelOffsetProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => Layout().Size;

    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        base.Render(context);
        var layout = Layout();

        // First, so the body covers the strich's end: it runs from the sign's middle to the pin.
        if (LabelOffset is { } offset)
        {
            var pin = new Point(SymbolCenter.X - offset.Dx, SymbolCenter.Y - offset.Dy);
            context.DrawLine(StrichHalo, SymbolCenter, pin);
            context.DrawLine(Strich, SymbolCenter, pin);
        }

        var body = Body();
        context.DrawGeometry(Red, null, body);
        using (context.PushGeometryClip(body))
        {
            context.DrawGeometry(null, InnerEdge, body);
        }

        context.DrawGeometry(null, Outline, body);
        context.DrawEllipse(null, Wheel, new Point(6, BodyHeight + 3.6), WheelRadius, WheelRadius);
        context.DrawEllipse(null, Wheel, new Point(SymbolWidth - 6, BodyHeight + 3.6), WheelRadius, WheelRadius);

        if (layout.Code is { } code)
        {
            context.DrawText(code, new Point((SymbolWidth - code.Width) / 2, Sag + ((BodyHeight - Sag - code.Height) / 2)));
        }

        foreach (var (text, origin) in layout.Lines)
        {
            context.FillRectangle(TextBackground, new Rect(origin.X - 2, origin.Y, text.Width + 4, text.Height), 2);
            context.DrawText(text, origin);
        }
    }

    private static StreamGeometry Body()
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(new Point(0, 0), isFilled: true);
            g.LineTo(new Point(0, BodyHeight));
            g.LineTo(new Point(SymbolWidth, BodyHeight));
            g.LineTo(new Point(SymbolWidth, 0));
            g.QuadraticBezierTo(new Point(SymbolWidth / 2, Sag * 2), new Point(0, 0));
            g.EndFigure(isClosed: true);
        }

        return geometry;
    }

    // Where each text goes: the code in the body, the Funkrufname right of it, Stärke and Status
    // stacked beneath. Recomputed per pass; a handful of signs on a map is cheap to lay out.
    private SignLayout Layout()
    {
        var bold = new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Bold);
        var regular = new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Normal);

        FormattedText? code = null;
        if (!string.IsNullOrWhiteSpace(VehicleType))
        {
            code = new FormattedText(VehicleType.Trim(), Culture, FlowDirection.LeftToRight, bold, 11, Brushes.White);
            code.MaxTextWidth = SymbolWidth - 6;
            code.MaxLineCount = 1;
            code.Trimming = TextTrimming.CharacterEllipsis;
        }

        var lines = new List<(FormattedText Text, Point Origin)>();
        var width = SymbolWidth;
        var height = SymbolHeight;
        if (!string.IsNullOrWhiteSpace(Title))
        {
            var title = new FormattedText(Title, Culture, FlowDirection.LeftToRight, bold, 12, TextInk);
            var origin = new Point(SymbolWidth + Gap + 2, (BodyHeight - title.Height) / 2);
            lines.Add((title, origin));
            width = Math.Max(width, origin.X + title.Width + 2);
        }

        var y = SymbolHeight + 1;
        foreach (var below in new[] { Strength, Status })
        {
            if (string.IsNullOrWhiteSpace(below))
            {
                continue;
            }

            var text = new FormattedText(below, Culture, FlowDirection.LeftToRight, regular, 10, TextInk);
            lines.Add((text, new Point(2, y)));
            width = Math.Max(width, text.Width + 4);
            y += text.Height;
            height = y;
        }

        return new SignLayout(code, lines, new Size(width, height));
    }

    private sealed record SignLayout(FormattedText? Code, IReadOnlyList<(FormattedText Text, Point Origin)> Lines, Size Size);
}
