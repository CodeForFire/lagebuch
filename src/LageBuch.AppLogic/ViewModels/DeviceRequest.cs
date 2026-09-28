namespace LageBuch.AppLogic.ViewModels;

// The first step of joining another device's hosted incident (§6, #459): which device to reach.
// Host is the target device's Tailscale name or IP; Pin is the share PIN the host displays. Who
// documents on this device is asked afterwards, from the host's own Stammdaten.
public sealed record DeviceRequest(string Host, string? Pin);
