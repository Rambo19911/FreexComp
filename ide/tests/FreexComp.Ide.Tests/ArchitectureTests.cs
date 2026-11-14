using System.Reflection;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Tests;

/// <summary>
/// ADR 008 / IDE-ARCH-093 for all three IDE assemblies. It lives here, not in the Core tests, because only this project
/// references <c>FreexComp.Ide</c>; IDE-ARCH-081 (<c>NoBlockingCallsInViewModels</c>) is in the Core tests.
/// </summary>
[Trait("Phase", "1")]
public sealed class ArchitectureTests
{
    /// <summary>Namespaces whose types open or prepare outbound connections (IDE-ARCH-093 plus the other BCL network stacks).</summary>
    private static readonly string[] NetworkNamespaces =
    [
        "System.Net.Http", "System.Net.Sockets", "System.Net.WebSockets", "System.Net.Mail", "System.Net.NetworkInformation",
        "System.Net.Quic", "System.Net.Security",
    ];

    /// <summary>The connection-making types of the <c>System.Net</c> namespace itself (it also holds harmless ones such as <c>WebUtility</c>).</summary>
    private static readonly string[] NetworkTypes =
    [
        "System.Net.WebRequest", "System.Net.HttpWebRequest", "System.Net.FtpWebRequest", "System.Net.WebClient",
        "System.Net.HttpListener", "System.Net.Dns", "System.Net.ServicePointManager",
    ];

    /// <summary>
    /// ADR 008 §2/§6: the one namespace per user-configured connection (<c>docs/network-allowlist.md</c>) that may use
    /// network types, with the card that added it. Empty in Phases 1–7 (IDE-ARCH-093).
    /// </summary>
    private static readonly Dictionary<string, string> AllowedNamespaces = new(StringComparer.Ordinal);

    private static readonly string[] IdeAssemblies = ["FreexComp.Ide.Core", "FreexComp.Designer", "FreexComp.Ide"];

    [Fact]
    [Trait("Req", "IDE-ARCH-093")]
    [Trait("Req", "ADR-008")]
    public void NoNetworkApis()
    {
        var used = new List<string>();
        var scanned = 0;
        foreach (var name in IdeAssemblies)
        {
            var assembly = Assembly.Load(name);
            var references = IlReferences.Read(assembly.Location);
            scanned += references.Count;
            used.AddRange(references
                .Where(r => IsNetwork(r.TargetType) && !AllowedNamespaces.ContainsKey(r.UserNamespace))
                .Select(r => $"{name}: {r.UserType} uses {r.TargetType}{(r.Member is null ? "" : "." + r.Member)}"));
            used.AddRange(assembly.GetReferencedAssemblies()
                .Select(a => a.Name ?? "")
                .Where(n => NetworkNamespaces.Any(ns => n == ns || n.StartsWith(ns + ".", StringComparison.Ordinal)))
                .Where(_ => AllowedNamespaces.Count == 0)
                .Select(n => $"{name}: references assembly {n}"));
        }

        Assert.True(scanned > 1000, $"only {scanned} references seen"); // the scanner sees the code (Designer has none yet)
        Assert.Empty(used.Distinct().Order(StringComparer.Ordinal));
    }

    private static bool IsNetwork(string type)
    {
        var outer = type.Split('+')[0];
        var lastDot = outer.LastIndexOf('.');
        var ns = lastDot < 0 ? "" : outer[..lastDot];
        return NetworkTypes.Contains(outer) || NetworkNamespaces.Any(n => ns == n || ns.StartsWith(n + ".", StringComparison.Ordinal));
    }
}
