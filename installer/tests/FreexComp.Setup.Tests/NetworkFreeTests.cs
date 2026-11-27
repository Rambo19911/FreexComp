using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

namespace FreexComp.Setup.Tests;

[Trait("Phase", "8")]
public sealed class NetworkFreeTests
{
    private static readonly string[] NetworkLibraries = ["ws2_32", "winhttp", "wininet", "urlmon"];

    /// <summary>
    /// INS-012: the setup assembly (the one the single-file setup bundles) references no type of a <c>System.Net</c>
    /// namespace and declares no P/Invoke into a networking library. The scan is proven not to be blind on this test
    /// assembly, which holds one such reference of each kind.
    /// </summary>
    [Fact]
    [Trait("Req", "INS-012")]
    public void SetupReferencesNoNetworkingApi()
    {
        var setup = Scan(typeof(SetupProgram).Assembly.Location);
        Assert.Empty(setup.Findings);
        Assert.True(setup.TypeReferences > 100, $"only {setup.TypeReferences} type references were read");
        Assert.Contains("System.IO.Compression.ZipArchive", setup.Types);
        Assert.Contains("shell32.dll", setup.Modules, StringComparer.OrdinalIgnoreCase);

        var self = Scan(typeof(NetworkFreeTests).Assembly.Location);
        Assert.Contains("type System.Net.Sockets.Socket", self.Findings);
        Assert.Contains("P/Invoke wininet.dll", self.Findings);
    }

    private sealed record ScanResult(List<string> Findings, List<string> Types, List<string> Modules, int TypeReferences);

    private static ScanResult Scan(string assembly)
    {
        using var stream = File.OpenRead(assembly);
        using var pe = new PEReader(stream);
        var md = pe.GetMetadataReader();
        var findings = new List<string>();
        var types = new List<string>();
        foreach (var handle in md.TypeReferences)
        {
            var reference = md.GetTypeReference(handle);
            var outer = reference;
            while (outer.ResolutionScope.Kind == HandleKind.TypeReference)
                outer = md.GetTypeReference((TypeReferenceHandle)outer.ResolutionScope);
            var ns = md.GetString(outer.Namespace);
            var name = ns.Length == 0 ? md.GetString(reference.Name) : ns + "." + md.GetString(reference.Name);
            types.Add(name);
            if (ns == "System.Net" || ns.StartsWith("System.Net.", StringComparison.Ordinal))
                findings.Add("type " + name);
        }

        var modules = Enumerable.Range(1, md.GetTableRowCount(TableIndex.ModuleRef))
            .Select(row => md.GetString(md.GetModuleReference(MetadataTokens.ModuleReferenceHandle(row)).Name)).ToList();
        foreach (var module in modules)
        {
            var bare = Path.GetFileNameWithoutExtension(module);
            if (NetworkLibraries.Contains(bare, StringComparer.OrdinalIgnoreCase))
                findings.Add("P/Invoke " + module);
        }

        foreach (var reference in md.AssemblyReferences.Select(h => md.GetString(md.GetAssemblyReference(h).Name)))
        {
            if (reference.StartsWith("System.Net", StringComparison.Ordinal))
                findings.Add("assembly " + reference);
        }

        return new ScanResult(findings, types, modules, md.TypeReferences.Count);
    }

    // the planted references of the scanner's self-check (never called)
    private static Type PlantedSocketType() => typeof(System.Net.Sockets.Socket);

    [DllImport("wininet.dll", EntryPoint = "InternetGetConnectedState")]
    private static extern bool PlantedNetworkImport(out int flags, int reserved);

    internal static object[] Planted() => [PlantedSocketType(), (Func<bool>)(() => PlantedNetworkImport(out _, 0))];
}
