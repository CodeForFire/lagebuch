using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using LageBuch.Domain;

namespace LageBuch.App.Shared.Controls;

/// <summary>
/// A unit's taktisches Zeichen after DV 102: the Kraftfahrzeug Grundzeichen in Feuerwehr red with
/// the mark of its Fahrzeugtyp inside (<see cref="VehicleTypes.SignFor"/>: the Löschfahrzeug's fork,
/// the DLK's ladder, the RTW's cross …) and its Kurzbezeichnung, and the Funkrufname to its right
/// (§1.6: "rechts daneben: Bezeichnung der Einheit"). The Stärke §1.6 puts beneath it, and the
/// Status, are left to the unit list beside the map, so a crowded map stays readable. Drawn rather than
/// loaded from SVG, after the CC0 set of jonas-koeritz/Taktische-Zeichen, so it needs no package
/// and draws the same on a phone. The sign's body sits at the top left, and from
/// <see cref="SymbolCenter"/> it draws the Verbindungsstrich back to the unit's pin, which stands
/// <see cref="MapMarkerPanel.ResolvedOffsetProperty"/> away (§1.5: a sign moved aside "mit Verbindungsstrichen zur jeweiligen
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

    private const double WheelRadius = 2.4;
    private const double SymbolHeight = BodyHeight + 7;
    private const double Gap = 4;

    // The body's top edge sags towards the middle, as on the DV 102 Kraftfahrzeug sign.
    private const double Sag = 3.5;

    // The source set draws on 256 units, its body from x 10 to 246 and y 64 to 192: 236 units
    // across our SymbolWidth, the same scale both ways.
    private const double Scale = SymbolWidth / 236;

    // A code above a mark's line, as the set writes "HLF" or "RTW": smaller, from the left edge.
    private const double SmallCodeSize = 8;
    private const double SmallCodeLeft = 1.8;

    private static readonly IBrush Red = new ImmutableSolidColorBrush(Color.FromRgb(0xFF, 0x00, 0x00));

    // Twice the edge's width: clipped to the body, only its inner half shows, just inside the outline.
    private static readonly IPen InnerEdge = new ImmutablePen(Brushes.White, 3.2);
    private static readonly IPen Outline = new ImmutablePen(Brushes.Black, 1);
    private static readonly IPen Wheel = new ImmutablePen(Brushes.Black, 1.2);
    private static readonly IPen Mark = new ImmutablePen(Brushes.White, 1.2);
    private static readonly IPen Ladder = new ImmutablePen(Brushes.White, 1.5);
    private static readonly IPen Rope = new ImmutablePen(Brushes.White, 2.8);
    private static readonly IPen RopeCore = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromRgb(0xFF, 0x00, 0x00)), 1.5);
    private static readonly IPen Frame = new ImmutablePen(Brushes.Black, 1);

    // Black, as DV 102 draws lines; a white halo keeps it visible on a dark tile or a forest.
    private static readonly IPen Strich = new ImmutablePen(Brushes.Black, 1.5);
    private static readonly IPen StrichHalo = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)), 3.5);

    private static readonly IBrush TextBackground = new ImmutableSolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF));
    private static readonly IBrush TextInk = new ImmutableSolidColorBrush(Color.FromRgb(0x21, 0x21, 0x21));
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("de-DE");

    static TacticalSignControl()
    {
        AffectsMeasure<TacticalSignControl>(VehicleTypeProperty, TitleProperty);
        AffectsRender<TacticalSignControl>(VehicleTypeProperty, TitleProperty, MapMarkerPanel.ResolvedOffsetProperty);
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

    protected override Size MeasureOverride(Size availableSize) => Layout().Size;

    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        base.Render(context);
        var layout = Layout();

        // First, so the body covers the strich's end: it runs from the sign's middle to the pin.
        if (MapMarkerPanel.GetResolvedOffset(this) is { } offset)
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

        var sign = VehicleTypes.SignFor(VehicleType);
        using (context.PushGeometryClip(body))
        {
            DrawMark(context, sign?.Mark ?? VehicleMark.Keines);
        }

        context.DrawGeometry(null, Outline, body);
        if (sign?.Mark == VehicleMark.Wechsellader)
        {
            context.DrawGeometry(null, Frame, Polyline(At(3, 63), At(3, 199), At(247, 199)));
        }

        context.DrawEllipse(null, Wheel, new Point(6, BodyHeight + 3.6), WheelRadius, WheelRadius);
        context.DrawEllipse(null, Wheel, new Point(SymbolWidth - 6, BodyHeight + 3.6), WheelRadius, WheelRadius);
        if (sign?.MiddleWheel == true)
        {
            context.DrawEllipse(null, Wheel, new Point(SymbolWidth / 2, BodyHeight + 3.6), WheelRadius, WheelRadius);
        }

        if (layout.Code is { } code)
        {
            var origin = layout.CodeIsSmall
                ? new Point(SmallCodeLeft, At(0, 128).Y - 1 - code.Baseline)
                : new Point((SymbolWidth - code.Width) / 2, Sag + ((BodyHeight - Sag - code.Height) / 2));
            context.DrawText(code, origin);
        }

        foreach (var (text, origin) in layout.Lines)
        {
            context.FillRectangle(TextBackground, new Rect(origin.X - 2, origin.Y, text.Width + 4, text.Height), 2);
            context.DrawText(text, origin);
        }
    }

    // The marks inside the body, traced from the set's SVGs in its 256-unit coordinates.
    private static void DrawMark(DrawingContext context, VehicleMark mark)
    {
        switch (mark)
        {
            case VehicleMark.Loeschfahrzeug:
                DrawFork(context);
                break;
            case VehicleMark.Schlauchwagen:
                DrawFork(context);
                context.DrawGeometry(null, Mark, Hose());
                break;
            case VehicleMark.Hubrettung:
                context.DrawGeometry(null, Ladder, Polyline(At(125, 192), At(128, 192), At(208, 112), At(228, 112), At(228, 92), At(208, 92), At(208, 112)));
                break;
            case VehicleMark.Ruestwagen:
                DrawWinch(context);
                break;
            case VehicleMark.Sanitaet:
                DrawCross(context);
                break;
            case VehicleMark.Notarzt:
                DrawCross(context);
                context.DrawLine(Mark, At(108, 160), At(148, 160));
                break;
            default:
                // The bare Grundzeichen; the Wechsellader's frame lies outside the body.
                break;
        }
    }

    private static void DrawFork(DrawingContext context)
    {
        context.DrawLine(Mark, At(10, 128), At(246, 128));
        context.DrawLine(Mark, At(156, 128), At(241, 187));
        context.DrawLine(Mark, At(156, 128), At(245, 65));
    }

    private static void DrawCross(DrawingContext context)
    {
        context.DrawLine(Mark, At(10, 128), At(246, 128));
        context.DrawLine(Mark, At(128, 192), At(128, 82));
    }

    // The winch's drum, its two stays to the rear, and the rope paying out forward in a wave,
    // drawn hollow: a white rope with a red core.
    private static void DrawWinch(DrawingContext context)
    {
        var rope = new StreamGeometry();
        using (var g = rope.Open())
        {
            g.BeginFigure(At(120, 152), isFilled: false);
            g.LineTo(At(130, 152));
            g.CubicBezierTo(At(140, 132), At(188, 132), At(178, 152));
            g.CubicBezierTo(At(168, 172), At(216, 172), At(226, 152));
            g.EndFigure(isClosed: false);
        }

        context.DrawGeometry(null, Rope, rope);
        context.DrawGeometry(null, RopeCore, rope);
        context.DrawRectangle(Red, Mark, new Rect(At(60, 140), At(120, 162)));
        context.DrawLine(Mark, At(60, 144), At(36, 134));
        context.DrawLine(Mark, At(60, 158), At(36, 168));
    }

    // Five half circles, alternating up and down: the hose the Schlauchwagen lays.
    private static StreamGeometry Hose()
    {
        var radius = 10 * Scale;
        var hose = new StreamGeometry();
        using (var g = hose.Open())
        {
            g.BeginFigure(At(25, 108), isFilled: false);
            for (var i = 0; i < 5; i++)
            {
                var direction = i % 2 == 0 ? SweepDirection.Clockwise : SweepDirection.CounterClockwise;
                g.ArcTo(At(45 + (i * 20.0), 108), new Size(radius, radius), 180, isLargeArc: true, direction);
            }

            g.EndFigure(isClosed: false);
        }

        return hose;
    }

    private static StreamGeometry Polyline(params Point[] points)
    {
        var line = new StreamGeometry();
        using (var g = line.Open())
        {
            g.BeginFigure(points[0], isFilled: false);
            foreach (var point in points.Skip(1))
            {
                g.LineTo(point);
            }

            g.EndFigure(isClosed: false);
        }

        return line;
    }

    // A point of the source set's 256-unit drawing, in this control's pixels.
    private static Point At(double x, double y) => new((x - 10) * Scale, (y - 64) * Scale);

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

    // Where each text goes: the code in the body, the Funkrufname right of it. Recomputed per pass; a handful of signs on a map is cheap to lay out.
    private SignLayout Layout()
    {
        var bold = new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Bold);

        // Where the code goes depends on the mark: in the middle of a bare sign, small above the
        // line of one that has a line, and nowhere on the ladder and the hose, which say it already.
        FormattedText? code = null;
        var sign = VehicleTypes.SignFor(VehicleType);
        var mark = sign?.Mark ?? VehicleMark.Keines;
        var codeIsSmall = mark is VehicleMark.Loeschfahrzeug or VehicleMark.Ruestwagen or VehicleMark.Sanitaet or VehicleMark.Notarzt;
        if (!string.IsNullOrWhiteSpace(VehicleType) && mark is not (VehicleMark.Hubrettung or VehicleMark.Schlauchwagen))
        {
            var size = codeIsSmall ? SmallCodeSize : 11;
            var room = mark switch
            {
                // Up to the fork, or to the cross's upright.
                VehicleMark.Loeschfahrzeug => At(156, 0).X - SmallCodeLeft - 1,
                VehicleMark.Sanitaet or VehicleMark.Notarzt => At(128, 0).X - SmallCodeLeft - 0.5,
                _ => SymbolWidth - 6,
            };
            code = new FormattedText(VehicleType.Trim(), Culture, FlowDirection.LeftToRight, bold, size, Brushes.White);

            // "HLF 20", too long for the room beside the fork, is drawn as the HLF it is rather than cut to "HL…".
            if (code.Width > room && sign is not null)
            {
                code = new FormattedText(sign.Code, Culture, FlowDirection.LeftToRight, bold, size, Brushes.White);
            }

            code.MaxTextWidth = room;
            code.MaxLineCount = 1;
            code.Trimming = TextTrimming.CharacterEllipsis;
        }

        var lines = new List<(FormattedText Text, Point Origin)>();
        var width = SymbolWidth;
        if (!string.IsNullOrWhiteSpace(Title))
        {
            var title = new FormattedText(Title, Culture, FlowDirection.LeftToRight, bold, 12, TextInk);
            var origin = new Point(SymbolWidth + Gap + 2, (BodyHeight - title.Height) / 2);
            lines.Add((title, origin));
            width = Math.Max(width, origin.X + title.Width + 2);
        }

        return new SignLayout(code, codeIsSmall, lines, new Size(width, SymbolHeight));
    }

    private sealed record SignLayout(FormattedText? Code, bool CodeIsSmall, IReadOnlyList<(FormattedText Text, Point Origin)> Lines, Size Size);
}
