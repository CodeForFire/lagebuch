using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Feuerwehr.Sync.Hosting;

/// <summary>
/// Picks a friendly IPv4 address to <em>show</em> the user as the one other devices dial. The host
/// itself binds every interface (<see cref="IPAddress.Any"/>), so this is purely cosmetic: it
/// prefers a Tailscale address (interface named <c>tailscale*</c>/<c>ts*</c>, or a <c>100.64.0.0/10</c>
/// CGNAT IP) when a tailnet is up, then any other private LAN IPv4, and finally falls back to
/// loopback so there is always something to show — two instances on one machine still reach each
/// other over <c>127.0.0.1</c>.
/// </summary>
public static class LocalNetwork
{
    /// <summary>The nicest dialable IPv4 to display; never null (loopback when nothing else is up).</summary>
    public static IPAddress DisplayAddress()
    {
        IPAddress? privateLan = null;
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up)
                continue;
            var looksLikeTailscale = nic.Name.StartsWith("tailscale", StringComparison.OrdinalIgnoreCase)
                || nic.Name.StartsWith("ts", StringComparison.OrdinalIgnoreCase);
            foreach (var addr in nic.GetIPProperties().UnicastAddresses)
            {
                var ip = addr.Address;
                if (ip.AddressFamily != AddressFamily.InterNetwork)
                    continue;
                // A tailnet address is the best answer — return the moment we see one.
                if (looksLikeTailscale || IsCarrierGradeNat(ip))
                    return ip;
                // Otherwise remember the first private LAN address as the fallback below Tailscale.
                if (privateLan is null && IsPrivateLan(ip))
                    privateLan = ip;
            }
        }
        return privateLan ?? IPAddress.Loopback;
    }

    // 100.64.0.0/10 — the CGNAT block Tailscale draws from.
    private static bool IsCarrierGradeNat(IPAddress address)
    {
        var b = address.GetAddressBytes();
        return b[0] == 100 && b[1] >= 64 && b[1] <= 127;
    }

    // RFC 1918 private ranges: 10/8, 172.16/12, 192.168/16.
    private static bool IsPrivateLan(IPAddress address)
    {
        var b = address.GetAddressBytes();
        return b[0] == 10
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
            || (b[0] == 192 && b[1] == 168);
    }
}
