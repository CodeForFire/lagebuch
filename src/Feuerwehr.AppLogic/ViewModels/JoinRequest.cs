using Feuerwehr.Domain;

namespace Feuerwehr.AppLogic.ViewModels;

// Collected in the operator popup when joining another device's hosted incident (§6). Host is the
// target device's Tailscale name or IP; Operator is who documents on this device.
public sealed record JoinRequest(SessionOperator Operator, string Host);
