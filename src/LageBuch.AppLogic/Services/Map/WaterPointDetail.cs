namespace LageBuch.AppLogic.Services.Map;

/// <summary>One row of a water point's card: "Durchmesser" – "DN 100".</summary>
public sealed record WaterPointDetail(string Label, string Value)
{
    public const string TypeLabel = "Bauart";
}
