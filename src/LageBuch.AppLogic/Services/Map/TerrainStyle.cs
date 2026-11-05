namespace LageBuch.AppLogic.Services.Map;

/// <summary>What a terrain layer needs beyond the heights: the Einsatzort's height, the colour range.</summary>
/// <param name="Layer">Which layer to draw.</param>
/// <param name="ReferenceHeight">The Einsatzort's height, for <see cref="TerrainLayer.RelativZumEinsatzort"/>.</param>
/// <param name="Low">The height drawn in the first colour of <see cref="TerrainLayer.Hoehenschichten"/>.</param>
/// <param name="High">The height drawn in its last colour.</param>
public sealed record TerrainStyle(TerrainLayer Layer, double? ReferenceHeight = null, double Low = 0, double High = 0);
