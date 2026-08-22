using LageBuch.Domain;

namespace LageBuch.AppLogic.ViewModels;

// Collected in the operator popup when joining another device's hosted incident (§6). Host is the
// target device's Tailscale name or IP; Pin is the share PIN the host displays; Operator is who
// documents on this device.
public sealed record JoinRequest(SessionOperator Operator, string Host, string? Pin);
