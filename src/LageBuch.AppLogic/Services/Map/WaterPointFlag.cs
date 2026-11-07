namespace LageBuch.AppLogic.Services.Map;

/// <summary>
/// The screen size of a flag marker: a mast <see cref="MastLength"/> pixels high whose foot is the
/// water point, carrying a body <see cref="BodyHeight"/> high that grows to the right.
/// <see cref="NominalWidth"/> is how wide a body with four digits comes out, which is what a tap
/// counts on, since the tap does not measure text.
/// </summary>
public sealed record WaterPointFlag(double MastLength, double BodyHeight, double NominalWidth);
