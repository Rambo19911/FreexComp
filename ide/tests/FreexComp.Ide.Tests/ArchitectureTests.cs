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

    private static bool IsTypeOrNested(string userType, string type) =>
        userType == type || userType.StartsWith(type + "+", StringComparison.Ordinal);

    private static IEnumerable<(string Assembly, IlReference Reference)> AllReferences() =>
        IdeAssemblies.SelectMany(name => IlReferences.Read(Assembly.Load(name).Location).Select(r => (name, r)));

    [Fact]
    [Trait("Req", "IDE-SET-003")]
    public void OnlyUserPathsAsksForOsFolders()
    {
        var callers = AllReferences()
            .Where(x => x.Reference.TargetType == "System.Environment" && x.Reference.Member == "GetFolderPath")
            .Select(x => x.Reference.UserType).Distinct().ToList();
        Assert.NotEmpty(callers);
        Assert.All(callers, t => Assert.True(IsTypeOrNested(t, "FreexComp.Ide.Core.Settings.UserPaths"), t));
    }

    [Fact]
    [Trait("Req", "IDE-ARCH-023")]
    public void DocumentsNeverTouchTheDisk()
    {
        // Content goes through IDocumentService; a document may only ask whether something exists (the Welcome page
        // marks missing recent projects, IDE-WEL-005).
        string[] disk = ["System.IO.File", "System.IO.FileInfo", "System.IO.FileStream", "System.IO.Directory", "System.IO.StreamReader",
            "System.IO.StreamWriter"];
        string[] contentAccess = ["ReadAllBytes", "WriteAllBytes", "WriteAllBytesDurable", "OverwriteFile", "ReplaceFile", "MoveFile", "DeleteFile",
            "DeleteDirectory", "CreateDirectory"];
        var documentTypes = AllReferences().Select(x => x.Reference)
            .Where(r => r.UserType.StartsWith("FreexComp.Ide.Core.ViewModels.", StringComparison.Ordinal)
                && r.UserType.Split('+')[0].EndsWith("DocumentViewModel", StringComparison.Ordinal))
            .ToList();
        Assert.Contains(documentTypes, r => r.UserType == "FreexComp.Ide.Core.ViewModels.TextDocumentViewModel");
        Assert.Empty(documentTypes.Where(r => disk.Contains(r.TargetType)
                || (r.TargetType == "FreexComp.Ide.Core.Platform.IFileSystem" && contentAccess.Contains(r.Member))).Select(r => $"{r.UserType} uses {r.TargetType}.{r.Member}").Distinct());
    }

    [Fact]
    [Trait("Req", "IDE-ARCH-101")]
    public void CompilerIsAnInProcessLibrary()
    {
        var core = Assembly.Load("FreexComp.Ide.Core").GetReferencedAssemblies().Select(a => a.Name).ToList();
        Assert.Contains("FreexComp.Compiler", core); // lexer + classifier for highlighting
        Assert.Contains("FreexComp.Build", core);    // the .pproj model

        // The only process the IDE starts in Phase 1 is the OS file manager; it never shells out to opc for editor features.
        var starters = AllReferences()
            .Where(x => x.Reference.TargetType == "System.Diagnostics.Process" && x.Reference.Member == "Start")
            .Select(x => x.Reference.UserType).Distinct().ToList();
        Assert.All(starters, t => Assert.True(IsTypeOrNested(t, "FreexComp.Ide.Platform.WindowsShellService"), t));
    }

    private static bool IsNetwork(string type)
    {
        var outer = type.Split('+')[0];
        var lastDot = outer.LastIndexOf('.');
        var ns = lastDot < 0 ? "" : outer[..lastDot];
        return NetworkTypes.Contains(outer) || NetworkNamespaces.Any(n => ns == n || ns.StartsWith(n + ".", StringComparison.Ordinal));
    }
}
