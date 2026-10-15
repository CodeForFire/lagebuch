namespace LageBuch.AppLogic.ViewModels;

/// <summary>One line of the Funkspruch as it is read out, with the spelling to give for it, if any.</summary>
/// <param name="Text">What is said.</param>
/// <param name="Spelling">How to spell it over the radio; set for the Einsatzstelle only, the part
/// whose street name the ILS most often asks to have spelled.</param>
public sealed record FunkspruchLine(string Text, string? Spelling);
