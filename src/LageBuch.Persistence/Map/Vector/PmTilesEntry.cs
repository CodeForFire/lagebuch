namespace LageBuch.Persistence.Map.Vector;

/// <summary>
/// One PMTiles directory entry: <paramref name="RunLength"/> consecutive tile ids sharing one blob,
/// or, with a run length of 0, a leaf directory to descend into.
/// </summary>
public readonly record struct PmTilesEntry(ulong TileId, uint RunLength, uint Length, ulong Offset);
