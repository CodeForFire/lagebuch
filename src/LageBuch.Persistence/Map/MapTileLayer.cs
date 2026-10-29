using LageBuch.Domain.ValueObjects;

namespace LageBuch.Persistence.Map;

/// <summary>
/// One Kartenpaket's tiles and the area they are complete in. Outside <see cref="Boundary"/> a
/// pack's tiles show empty land, because its OSM data was cut to that area before rendering; null
/// for a pack whose area is unknown, which is drawn whole.
/// </summary>
/// <param name="Tiles">The pack's raster tiles.</param>
/// <param name="Boundary">The area's rings, outer rings and holes alike, to be filled even-odd.</param>
public sealed record MapTileLayer(IMapTileSource Tiles, IReadOnlyList<IReadOnlyList<GeoPoint>>? Boundary);
