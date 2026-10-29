using Avalonia;
using Avalonia.Media;

namespace LageBuch.App.Shared.Controls;

/// <summary>
/// A name a vector tile wants on the map, anchored in that tile's pixels. The map draws the labels
/// of all tiles in one pass after the tiles, so a name is never cut where its tile ends and two
/// names never overlap; on a turned map it keeps every name readable.
/// </summary>
/// <param name="Text">What it says; a name repeated close by is drawn once.</param>
/// <param name="Outline">The glyphs, laid out from the origin.</param>
/// <param name="TextSize">The space the glyphs take before they are turned.</param>
/// <param name="Anchor">The point on the tile the text is centred on.</param>
/// <param name="Angle">
/// The direction of the street the name runs along, in radians clockwise from east on the tile;
/// null for a name that stays level, such as a place's.
/// </param>
/// <param name="Priority">Lower is placed first and wins a collision: a town before a street, a street before a house number.</param>
/// <param name="Ink">The fill of the glyphs.</param>
/// <param name="Halo">The white edge around the glyphs, or null for none.</param>
public sealed record MapLabel(string Text, Geometry Outline, Size TextSize, Point Anchor, double? Angle, int Priority, IBrush Ink, IPen? Halo);
