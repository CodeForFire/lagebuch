using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using LageBuch.AppLogic.Services.Map;
using LageBuch.Domain.ValueObjects;
using LageBuch.Persistence.Map;

namespace LageBuch.App.Shared.Controls;

/// <summary>
/// Paints the tiles around a centre at a zoom into a rectangle. One routine for the live map and,
/// later, its snapshot in the Bericht, so the two cannot disagree about where a tile goes.
/// </summary>
public static class MapDrawing
{
    /// <summary>A street named again closer than this, in pixels, is named once.</summary>
    public const double RepeatDistance = 256;

    // Free space kept around a label, so two names never touch.
    private const double LabelGap = 2;

    /// <summary>
    /// The map around <paramref name="center"/> at <paramref name="zoom"/>, turned clockwise by
    /// <paramref name="rotation"/> degrees about the middle of <paramref name="size"/>. Names are
    /// laid over it upright, a street's name along its street, and the
    /// <paramref name="waterPoints"/> over those: a name that would run under a water point's
    /// label is left out, since the label is what the crew is looking for. The
    /// <paramref name="terrain"/> layer lies between the tiles and the names, and the
    /// <paramref name="overlays"/> over the terrain, under the names, and the
    /// <paramref name="flowPath"/> over the Kartenebenen. All of it but the terrain, the
    /// Kartenebenen and the Fließweg in the colours of the cache's <see cref="MapTileCache.Style"/>.
    /// </summary>
    public static void Draw(DrawingContext context, MapTileCache tiles, GeoPoint center, int zoom, Size size, double rotation = 0, IReadOnlyList<WaterPoint>? waterPoints = null, WaterPoint? selected = null, TerrainTileCache? terrain = null, IReadOnlyList<OverlayShape>? overlays = null, IReadOnlyList<GeoPoint>? flowPath = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(tiles);
        ArgumentNullException.ThrowIfNull(center);
        if (size.Width <= 0 || size.Height <= 0)
        {
            return;
        }

        var style = tiles.Style;
        context.FillRectangle(style.Background, new Rect(size));
        var marks = waterPoints is null ? [] : WaterPointMarkers.LayOut(waterPoints, center, zoom, size, selected, rotation);
        var labels = new List<(MapLabel Label, Point At)>();
        if (tiles.Source is { } source)
        {
            // Several packs are drawn one over the other. Each pack's tiles are cut to its area when
            // it is built, so outside it a pack draws nothing that could cover its neighbour's streets.
            IReadOnlyList<IMapTileSource> layers = source is CombinedMapTileSource combined ? combined.Layers : [source];
            var turn = Turn(size, rotation);
            foreach (var layer in layers)
            {
                DrawLayer(context, tiles, layer, center, zoom, size, turn, labels);
            }

            if (terrain is not null && zoom >= TerrainSampler.MinZoom)
            {
                DrawTerrain(context, terrain, center, zoom, size, turn, rotation);
            }
        }

        // Kartenebenen lie on the map and its terrain but under its names and the water points, so
        // a plume never hides the street it reaches or the hydrant next to it.
        if (overlays is { Count: > 0 })
        {
            DrawOverlays(context, overlays, center, zoom, size, rotation, style);
        }

        // The Fließweg over a Kartenebene, so the route a spill takes stays visible inside a plume.
        // A route of one point -- water that stays where it is, on flat ground -- is just its end dot.
        if (flowPath is { Count: > 0 })
        {
            DrawFlowPath(context, flowPath, center, zoom, size, rotation);
        }

        if (labels.Count > 0)
        {
            // The names of every vector tile, over all tiles and unclipped, so none is cut at a tile's
            // edge. Only a water point's flag keeps names away: a bare disc is drawn over the name.
            var reserved = marks.Where(m => m.HasText).Select(m => m.Bounds).ToList();
            foreach (var (label, placement) in PlaceLabels(labels, size, rotation, reserved))
            {
                using (context.PushTransform(placement))
                {
                    if (label.Halo is { } halo)
                    {
                        context.DrawGeometry(null, halo, label.Outline);
                    }

                    context.DrawGeometry(label.Ink, null, label.Outline);
                }
            }
        }

        foreach (var mark in marks)
        {
            WaterPointMarkers.Draw(context, mark, style.SelectedRing);
        }
    }

    /// <summary>
    /// Which of the tiles' labels to draw, in order, each with where it goes on screen: the most
    /// important first, and each only if it lies on the screen, overlaps none placed before it and
    /// does not repeat a name placed within <see cref="RepeatDistance"/>. <paramref name="labels"/>
    /// pairs each label with the point on screen it belongs to; on a map turned by
    /// <paramref name="rotation"/> degrees a street's name turns with its street, never upside
    /// down, and every other name stays level. No name overlaps a rectangle of
    /// <paramref name="reserved"/>, the screen space of the water points' labels.
    /// </summary>
    public static IReadOnlyList<(MapLabel Label, Matrix Placement)> PlaceLabels(IEnumerable<(MapLabel Label, Point At)> labels, Size size, double rotation = 0, IReadOnlyList<Rect>? reserved = null)
    {
        ArgumentNullException.ThrowIfNull(labels);
        var screen = new Rect(size);
        var placed = new List<(MapLabel Label, Point Center)>();
        var result = new List<(MapLabel Label, Matrix Placement)>();
        var taken = new List<Rect>(reserved ?? []);
        foreach (var (label, at) in labels.OrderBy(l => l.Label.Priority))
        {
            var placement = Placement(label, at, rotation);
            var bounds = new Rect(label.TextSize).TransformToAABB(placement);
            if (!screen.Intersects(bounds)
                || taken.Exists(t => t.Intersects(bounds.Inflate(LabelGap)))
                || placed.Exists(p => string.Equals(p.Label.Text, label.Text, StringComparison.Ordinal) && Distance(p.Center, bounds.Center) < RepeatDistance))
            {
                continue;
            }

            placed.Add((label, bounds.Center));
            result.Add((label, placement));
            taken.Add(bounds);
        }

        return result;
    }

    // The text centred on `at`, or its pin on it: level, or along its street turned with the map, the right way up.
    private static Matrix Placement(MapLabel label, Point at, double rotation)
    {
        var angle = 0.0;
        if (label.Angle is { } along)
        {
            angle = Math.IEEERemainder(along + (rotation * Math.PI / 180), 2 * Math.PI);
            if (angle > Math.PI / 2)
            {
                angle -= Math.PI;
            }
            else if (angle < -Math.PI / 2)
            {
                angle += Math.PI;
            }
        }

        var pin = label.Pin ?? new Point(label.TextSize.Width / 2, label.TextSize.Height / 2);
        return Matrix.CreateTranslation(-pin.X, -pin.Y) * Matrix.CreateRotation(angle) * Matrix.CreateTranslation(at.X, at.Y);
    }

    private static double Distance(Point a, Point b) => Math.Sqrt(((b.X - a.X) * (b.X - a.X)) + ((b.Y - a.Y) * (b.Y - a.Y)));

    // Turns the north-up map clockwise by `rotation` degrees about the middle of the screen.
    private static Matrix Turn(Size size, double rotation)
    {
        var middle = new Vector(size.Width / 2, size.Height / 2);
        return Matrix.CreateTranslation(-middle) * Matrix.CreateRotation(rotation * Math.PI / 180) * Matrix.CreateTranslation(middle);
    }

    private static void DrawLayer(DrawingContext context, MapTileCache tiles, IMapTileSource layer, GeoPoint center, int zoom, Size size, Matrix turn, List<(MapLabel Label, Point At)> labels)
    {
        var (centerX, centerY) = WebMercator.ToWorldPixel(center, zoom);
        var left = centerX - (size.Width / 2);
        var top = centerY - (size.Height / 2);

        // Turned, the screen shows a tilted square of the north-up map: draw every tile it touches.
        var shown = turn.TryInvert(out var back) ? new Rect(size).TransformToAABB(back) : new Rect(size);
        var (firstX, firstY) = WebMercator.ToTileIndex(left + shown.Left, top + shown.Top);
        var (lastX, lastY) = WebMercator.ToTileIndex(left + shown.Right, top + shown.Bottom);
        var tilesPerSide = 1 << zoom;

        using (context.PushTransform(turn))
        {
            for (var tx = Math.Max(firstX, 0); tx <= Math.Min(lastX, tilesPerSide - 1); tx++)
            {
                for (var ty = Math.Max(firstY, 0); ty <= Math.Min(lastY, tilesPerSide - 1); ty++)
                {
                    var target = new Rect(
                        (tx * (double)WebMercator.TileSizePixels) - left,
                        (ty * (double)WebMercator.TileSizePixels) - top,
                        WebMercator.TileSizePixels,
                        WebMercator.TileSizePixels);

                    // Past the pack's deepest zoom the cache draws the ancestor's part of the tile at
                    // this zoom, so a missing tile here is one the pack does not have.
                    if (tiles.Get(layer, zoom, tx, ty) is { } bitmap)
                    {
                        context.DrawImage(bitmap, target);
                        labels.AddRange(tiles.Labels(layer, zoom, tx, ty).Select(l => (l, turn.Transform(target.TopLeft + l.Anchor))));
                    }
                }
            }
        }
    }

    // Brown like a topographic map's contours; the relative layer's lines in grey, its Einsatzort line black.
    private static readonly IPen ContourPen = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(0xD9, 0x9A, 0x6B, 0x3F)), 0.7);
    private static readonly IPen ZaehllinienPen = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromRgb(0x7A, 0x4A, 0x1F)), 1.4);
    private static readonly IPen RelativePen = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(0x99, 0x33, 0x33, 0x33)), 0.6);
    private static readonly IPen ReferencePen = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromRgb(0x21, 0x21, 0x21)), 1.6);
    private static readonly IBrush ContourLabelBrush = new ImmutableSolidColorBrush(Color.FromRgb(0x5D, 0x37, 0x17));
    private static readonly IBrush RelativeLabelBrush = new ImmutableSolidColorBrush(Color.FromRgb(0x21, 0x21, 0x21));
    private static readonly IPen ContourLabelHalo = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)), 3, lineJoin: PenLineJoin.Round);

    // The terrain tiles over the map's, turned with it: first every image, then the lines, then the
    // lines' labels -- so a neighbouring tile's image never covers a line or a label. Past
    // TerrainSampler.MaxZoom a tile of that zoom is drawn enlarged: finer than the DGM's 5 m cells,
    // a finer tile would only show the cells' edges.
    private static void DrawTerrain(DrawingContext context, TerrainTileCache terrain, GeoPoint center, int zoom, Size size, Matrix turn, double rotation)
    {
        if (terrain.Style is not { } style)
        {
            return;
        }

        var (centerX, centerY) = WebMercator.ToWorldPixel(center, zoom);
        var left = centerX - (size.Width / 2);
        var top = centerY - (size.Height / 2);
        var shown = turn.TryInvert(out var back) ? new Rect(size).TransformToAABB(back) : new Rect(size);
        var (firstX, firstY) = WebMercator.ToTileIndex(left + shown.Left, top + shown.Top);
        var (lastX, lastY) = WebMercator.ToTileIndex(left + shown.Right, top + shown.Bottom);
        var sourceZoom = Math.Min(zoom, TerrainSampler.MaxZoom);
        var levels = zoom - sourceZoom;
        var scale = (double)(1 << levels);
        var side = WebMercator.TileSizePixels * scale;
        var tilesPerSide = 1 << sourceZoom;
        var (fromX, toX) = (Math.Max(firstX >> levels, 0), Math.Min(lastX >> levels, tilesPerSide - 1));
        var (fromY, toY) = (Math.Max(firstY >> levels, 0), Math.Min(lastY >> levels, tilesPerSide - 1));
        terrain.Reserve(Math.Max(0, toX - fromX + 1) * Math.Max(0, toY - fromY + 1));
        var drawn = new List<(Point Origin, DrawnTerrainTile Tile)>();
        for (var tx = fromX; tx <= toX; tx++)
        {
            for (var ty = fromY; ty <= toY; ty++)
            {
                if (terrain.Get(sourceZoom, tx, ty) is { } tile)
                {
                    drawn.Add((new Point((tx * side) - left, (ty * side) - top), tile));
                }
            }
        }

        var relative = style.Layer == TerrainLayer.RelativZumEinsatzort;

        // The lines are drawn through the enlargement, so their pens are made thinner by as much.
        IPen Thin(IPen pen) => levels == 0 ? pen : new ImmutablePen((IImmutableBrush?)pen.Brush, pen.Thickness / scale);
        var pens = (Reference: Thin(ReferencePen), Relative: Thin(RelativePen), Zaehllinie: Thin(ZaehllinienPen), Contour: Thin(ContourPen));

        // Smoothed only when enlarged: at 1:1 a filter blends each tile's edge with the nothing beyond it, and draws a seam.
        using (context.PushTransform(turn))
        using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = levels == 0 ? Avalonia.Media.Imaging.BitmapInterpolationMode.None : Avalonia.Media.Imaging.BitmapInterpolationMode.HighQuality }))
        {
            foreach (var (origin, tile) in drawn)
            {
                if (tile.Image is { } image)
                {
                    context.DrawImage(image, new Rect(origin, new Size(side, side)));
                }
            }

            foreach (var (origin, tile) in drawn)
            {
                using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(origin.X, origin.Y)))
                {
                    foreach (var line in tile.Contours)
                    {
                        var pen = (relative, line.IsMajor) switch
                        {
                            (true, true) => pens.Reference,
                            (true, false) => pens.Relative,
                            (false, true) => pens.Zaehllinie,
                            _ => pens.Contour,
                        };
                        context.DrawGeometry(null, pen, line.Lines);
                    }
                }
            }

            // A label that would touch one already drawn is left out; the line still shows.
            var taken = new List<Rect>();
            foreach (var (origin, tile) in drawn)
            {
                foreach (var label in tile.Contours.Select(c => c.Label).OfType<TerrainLabel>())
                {
                    // Along its line, but never upside down on the turned map.
                    var angle = label.Angle;
                    var onScreen = Math.IEEERemainder(angle + (rotation * Math.PI / 180), 2 * Math.PI);
                    if (onScreen is > Math.PI / 2 or < -Math.PI / 2)
                    {
                        angle += Math.PI;
                    }

                    var at = Matrix.CreateRotation(angle) * Matrix.CreateTranslation(origin.X + (label.At.X * scale), origin.Y + (label.At.Y * scale));
                    var bounds = label.Outline.Bounds.TransformToAABB(at).Inflate(4);
                    if (taken.Exists(t => t.Intersects(bounds)))
                    {
                        continue;
                    }

                    taken.Add(bounds);
                    using (context.PushTransform(at))
                    {
                        context.DrawGeometry(null, ContourLabelHalo, label.Outline);
                        context.DrawGeometry(relative ? RelativeLabelBrush : ContourLabelBrush, null, label.Outline);
                    }
                }
            }
        }
    }

    private static readonly IPen PointOutline = new ImmutablePen(Brushes.White, 1.5);

    // Every shape whose bounds reach the screen, in the order the file gave them: areas filled
    // even-odd so a hole stays open, lines stroked, points as dots with their name close up. The
    // shapes keep the colours their file gave them by day and by night; a name sits in the same
    // box a water point's does, so it reads on the Nachtkarte too.
    private static void DrawOverlays(DrawingContext context, IReadOnlyList<OverlayShape> shapes, GeoPoint center, int zoom, Size size, double rotation, MapStyle style)
    {
        const double margin = 40;
        Point[] corners = [new(-margin, -margin), new(size.Width + margin, -margin), new(-margin, size.Height + margin), new(size.Width + margin, size.Height + margin)];
        var view = GeoBounds.Of(corners.Select(c => ToGeo(center, zoom, size, c, rotation)));
        var (centerX, centerY) = WebMercator.ToWorldPixel(center, zoom);
        var turn = Turn(size, rotation);
        Point Screen(GeoPoint p)
        {
            var (x, y) = WebMercator.ToWorldPixel(p, zoom);
            return turn.Transform(new Point(x - centerX + (size.Width / 2), y - centerY + (size.Height / 2)));
        }

        foreach (var shape in shapes)
        {
            if (!shape.Bounds.Intersects(view))
            {
                continue;
            }

            var fill = new ImmutableSolidColorBrush(Color.FromUInt32(shape.Style.FillArgb));
            var stroke = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromUInt32(shape.Style.StrokeArgb)), shape.Style.StrokeWidth);
            if (shape.Kind == OverlayShapeKind.Point)
            {
                foreach (var part in shape.Parts)
                {
                    var at = Screen(part[0]);
                    context.DrawEllipse(new ImmutableSolidColorBrush(Color.FromUInt32(shape.Style.StrokeArgb)), PointOutline, at, 6, 6);
                    if (shape.Label is { } label && zoom >= WaterPointLayer.LabelsMinZoom)
                    {
                        var text = new FormattedText(label, LabelCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold), 11, style.WaterPointLabelText);
                        var origin = new Point(at.X + 10, at.Y - (text.Height / 2));
                        context.FillRectangle(style.WaterPointLabelBackground, new Rect(origin.X - 2, origin.Y, text.Width + 4, text.Height), 2);
                        context.DrawText(text, origin);
                    }
                }

                continue;
            }

            var area = shape.Kind == OverlayShapeKind.Area;
            var geometry = new StreamGeometry();
            using (var path = geometry.Open())
            {
                path.SetFillRule(FillRule.EvenOdd);
                foreach (var part in shape.Parts)
                {
                    path.BeginFigure(Screen(part[0]), isFilled: area);
                    for (var i = 1; i < part.Count; i++)
                    {
                        path.LineTo(Screen(part[i]));
                    }

                    path.EndFigure(isClosed: area);
                }
            }

            context.DrawGeometry(area ? fill : null, stroke, geometry);
        }
    }

    // The Fließweg: a blue line on a white casing, so it reads on blue water and dark relief alike.
    private static readonly IPen FlowCasing = new ImmutablePen(Brushes.White, 7, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
    private static readonly IPen FlowLine = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromRgb(0x0D, 0x47, 0xA1)), 3.5, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
    private static readonly IPen FlowArrow = new ImmutablePen(Brushes.White, 2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
    private static readonly IBrush FlowEnd = new ImmutableSolidColorBrush(Color.FromRgb(0x0D, 0x47, 0xA1));

    /// <summary>A chevron on the Fließweg every this many pixels, pointing downhill.</summary>
    public const double FlowArrowSpacing = 90;

    // The Fließweg on screen: the line, a chevron every FlowArrowSpacing pixels pointing the way the
    // water runs, and a dot where it ends.
    private static void DrawFlowPath(DrawingContext context, IReadOnlyList<GeoPoint> path, GeoPoint center, int zoom, Size size, double rotation)
    {
        var points = path.Select(p => ToScreen(center, zoom, size, p, rotation)).ToList();
        var line = new StreamGeometry();
        using (var g = line.Open())
        {
            g.BeginFigure(points[0], isFilled: false);
            foreach (var p in points.Skip(1))
            {
                g.LineTo(p);
            }

            g.EndFigure(isClosed: false);
        }

        context.DrawGeometry(null, FlowCasing, line);
        context.DrawGeometry(null, FlowLine, line);

        var travelled = 0.0;
        var nextArrow = FlowArrowSpacing / 2;
        for (var i = 1; i < points.Count; i++)
        {
            var (a, b) = (points[i - 1], points[i]);
            var length = Distance(a, b);
            while (length > 0 && travelled + length >= nextArrow)
            {
                var t = (nextArrow - travelled) / length;
                var at = new Point(a.X + ((b.X - a.X) * t), a.Y + ((b.Y - a.Y) * t));
                var angle = Math.Atan2(b.Y - a.Y, b.X - a.X);
                using (context.PushTransform(Matrix.CreateRotation(angle) * Matrix.CreateTranslation(at.X, at.Y)))
                {
                    context.DrawLine(FlowArrow, new Point(-3, -3.5), new Point(1.5, 0));
                    context.DrawLine(FlowArrow, new Point(1.5, 0), new Point(-3, 3.5));
                }

                nextArrow += FlowArrowSpacing;
            }

            travelled += length;
        }

        context.DrawEllipse(FlowEnd, FlowCasing, points[^1], 6, 6);
    }

    private static readonly CultureInfo LabelCulture = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>
    /// Where <paramref name="point"/> lands on a map of <paramref name="size"/> centred on
    /// <paramref name="center"/> and turned clockwise by <paramref name="rotation"/> degrees.
    /// </summary>
    public static Point ToScreen(GeoPoint center, int zoom, Size size, GeoPoint point, double rotation = 0)
    {
        var (centerX, centerY) = WebMercator.ToWorldPixel(center, zoom);
        var (x, y) = WebMercator.ToWorldPixel(point, zoom);
        return Turn(size, rotation).Transform(new Point(x - centerX + (size.Width / 2), y - centerY + (size.Height / 2)));
    }

    /// <summary>The inverse of <see cref="ToScreen"/>.</summary>
    public static GeoPoint ToGeo(GeoPoint center, int zoom, Size size, Point screen, double rotation = 0)
    {
        var (centerX, centerY) = WebMercator.ToWorldPixel(center, zoom);
        var northUp = Turn(size, -rotation).Transform(screen);
        return WebMercator.ToGeo(centerX + northUp.X - (size.Width / 2), centerY + northUp.Y - (size.Height / 2), zoom);
    }

    /// <summary>
    /// A movement of <paramref name="screen"/> pixels on a map turned by <paramref name="rotation"/>
    /// degrees, as the same movement on the north-up map: what a pan has to move the centre by.
    /// </summary>
    public static Vector ToNorthUp(Vector screen, double rotation) =>
        Matrix.CreateRotation(-rotation * Math.PI / 180).Transform(new Point(screen.X, screen.Y)) - default(Point);
}
