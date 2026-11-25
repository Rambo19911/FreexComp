using System.Net;
using System.Net.Sockets;
using FreexComp.Tests.Shared;

namespace FreexComp.E2E.Tests;

/// <summary>NET-020, NET-021: the network probe is not blind (loopback only, ADR 008 §6). In the end-to-end project: the policy project is the fixture of the endpoint scan (NET-012).</summary>
[Trait("Phase", "8")]
[Collection(NetworkProbeCollection.Name)]
public sealed class NetworkProbeTests
{
    [Fact]
    [Trait("Req", "NET-020")]
    [Trait("Req", "NET-021")]
    public async Task ProbeSeesLoopbackConnect()
    {
        using var probe = new NetworkProbe();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var accept = listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port, TestContext.Current.CancellationToken);
            using var server = await accept;
        }
        finally
        {
            listener.Stop();
        }

        Assert.Contains(probe.Events, e => e.Source == "System.Net.Sockets");
    }

    [Fact]
    [Trait("Req", "NET-020")]
    public void ProbeIsEmptyWithoutNetworkUse()
    {
        using var probe = new NetworkProbe();
        _ = Path.Combine(Path.GetTempPath(), "x");
        Assert.Empty(probe.Events);
    }
}
