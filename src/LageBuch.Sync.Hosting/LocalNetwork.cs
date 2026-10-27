using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace LageBuch.Sync.Hosting;

/// <summary>
/// Picks a friendly IPv4 address to <em>show</em> the user as the one other devices dial. The host
/// itself binds every interface (<see cref="IPAddress.Any"/>), so this is purely cosmetic: it shows
/// the first private LAN IPv4 and falls back to loopback so there is always something to show — two
/// instances on one machine still reach each other over <c>127.0.0.1</c>.
/// </summary>
public static class LocalNetwork
{
    /// <summary>The nicest dialable IPv4 to display; never null (loopback when nothing else is up).</summary>
    public static IPAddress DisplayAddress()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            foreach (var addr in nic.GetIPProperties().UnicastAddresses)
            {
                var ip = addr.Address;
                if (ip.AddressFamily == AddressFamily.InterNetwork && IsPrivateLan(ip))
                {
                    return ip;
                }
            }
        }

        return IPAddress.Loopback;
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
