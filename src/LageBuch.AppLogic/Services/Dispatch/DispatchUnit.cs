using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.Services.Dispatch;

/// <summary>One alarmed Einsatzmittel to record, with the Stammdaten vehicle it matched, if any.</summary>
public sealed record DispatchUnit(string Name, Vehicle? Vehicle);
