namespace FreexComp.E2E.Tests;

/// <summary>
/// The network probe listens to the whole process: the probe's own loopback self-test never runs alongside a test that
/// asserts no network use (L-012).
/// </summary>
[CollectionDefinition(Name)]
public sealed class NetworkProbeCollection
{
    public const string Name = "NetworkProbe";
}
