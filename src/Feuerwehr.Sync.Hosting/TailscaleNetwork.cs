using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Feuerwehr.Sync.Hosting;

/// <summary>
/// Finds the device's Tailscale address to bind the host on. Tailscale hands each device a
/// <c>100.64.0.0/10</c> (CGNAT) IPv4 on an interface named like <c>tailscale*</c>/<c>ts*</c>. The app
/// does not manage Tailscale — it only detects the interface and, when absent, lets the caller
/// surface "Tailscale nicht verbunden" rather than binding to the wrong network.
/// </summary>
public static class TailscaleNetwork
{
    public static IPAddress? LocalAddress()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up)
                continue;
            var looksLikeTailscale = nic.Name.StartsWith("tailscale", StringComparison.OrdinalIgnoreCase)
                || nic.Name.StartsWith("ts", StringComparison.OrdinalIgnoreCase);
            foreach (var addr in nic.GetIPProperties().UnicastAddresses)
            {
                if (addr.Address.AddressFamily != AddressFamily.InterNetwork)
                    continue;
                if (looksLikeTailscale || IsCarrierGradeNat(addr.Address))
                    return addr.Address;
            }
        }
        return null;
    }

    public static bool IsConnected() => LocalAddress() is not null;

    // 100.64.0.0/10 — the CGNAT block Tailscale draws from.
    private static bool IsCarrierGradeNat(IPAddress address)
    {
        var b = address.GetAddressBytes();
        return b[0] == 100 && b[1] >= 64 && b[1] <= 127;
    }
}
