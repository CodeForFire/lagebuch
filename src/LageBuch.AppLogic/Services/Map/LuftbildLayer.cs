namespace LageBuch.AppLogic.Services.Map;

/// <summary>What LUFTBILD LADEN fetches from the LDBV.</summary>
public enum LuftbildLayer
{
    /// <summary>The DOP40 Digitales Orthophoto, in colour, as a JPEG.</summary>
    Luftbild,

    /// <summary>The Parzellarkarte's Flurstücksgrenzen alone, yellow on transparent, as a PNG.</summary>
    Flurstuecke,
}
