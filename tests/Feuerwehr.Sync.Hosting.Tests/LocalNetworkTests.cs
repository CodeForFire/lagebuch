using System.Net;
using System.Net.Sockets;

namespace Feuerwehr.Sync.Hosting.Tests;

public class LocalNetworkTests
{
    [Fact]
    public void DisplayAddress_is_a_usable_ipv4_and_never_null()
    {
        var address = LocalNetwork.DisplayAddress();

        Assert.NotNull(address);
        Assert.Equal(AddressFamily.InterNetwork, address.AddressFamily);
        // Whatever the machine has, the result is dialable: either loopback (nothing else up) or a
        // routable/private interface address — never 0.0.0.0, which is a bind target, not a dial target.
        Assert.NotEqual(IPAddress.Any, address);
    }
}
