using Avalonia;
using Avalonia.Media;

namespace LageBuch.App.Shared.Controls;

/// <summary>
/// A name a vector tile wants on the map, in that tile's pixels. The map draws the labels of all
/// tiles in one pass after the tiles, so a name is never cut where its tile ends and two names
/// never overlap.
/// </summary>
/// <param name="Text">What it says; a name repeated close by is drawn once.</param>
/// <param name="Outline">The glyphs, laid out from the origin.</param>
/// <param name="Placement">From the glyphs' origin to the tile's pixels: turned along a road, centred on a place.</param>
/// <param name="Bounds">The space it takes on the tile, turned with it.</param>
/// <param name="Priority">Lower is placed first and wins a collision: a town before a street, a street before a house number.</param>
/// <param name="Ink">The fill of the glyphs.</param>
/// <param name="Halo">The white edge around the glyphs, or null for none.</param>
public sealed record MapLabel(string Text, Geometry Outline, Matrix Placement, Rect Bounds, int Priority, IBrush Ink, IPen? Halo);
