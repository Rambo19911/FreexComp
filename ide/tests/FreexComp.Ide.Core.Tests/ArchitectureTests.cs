using System.Reflection;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests;

// IDE-ARCH-001: Core and Designer must not reference any Avalonia, AvaloniaEdit or Dock assembly.
public sealed class ArchitectureTests
{
    [Fact]
    [Trait("Phase", "1")]
    [Trait("Req", "IDE-ARCH-001")]
    [Trait("Req", "IDE-ARCH-052")]
    public void CoreHasNoUiReferences()
    {
        Assert.Empty(UiReferences(Assembly.Load("FreexComp.Ide.Core")));
    }

    [Fact]
    [Trait("Phase", "1")]
    [Trait("Req", "IDE-ARCH-001")]
    public void DesignerHasNoUiReferences()
    {
        Assert.Empty(UiReferences(Assembly.Load("FreexComp.Designer")));
    }

    /// <summary>
    /// IDE-ARCH-004 (Phase 1 gate N2): services may use view-models only through interfaces; the concrete view-models
    /// are created by their owners outside <c>Services</c>.
    /// </summary>
    [Fact]
    [Trait("Phase", "1")]
    [Trait("Req", "IDE-ARCH-004")]
    public void ServicesUseViewModelInterfacesOnly()
    {
        var core = typeof(Composition.IdeServices).Assembly;
        var concrete = IlReferences.Read(core.Location)
            .Where(r => r.UserNamespace.StartsWith("FreexComp.Ide.Core.Services", StringComparison.Ordinal))
            .Where(r => r.TargetType.StartsWith("FreexComp.Ide.Core.ViewModels.", StringComparison.Ordinal))
            .Where(r => core.GetType(r.TargetType) is not { IsInterface: true })
            .Select(r => $"{r.UserType} uses {r.TargetType}{(r.Member is null ? "" : "." + r.Member)}")
            .Distinct().Order(StringComparer.Ordinal).ToList();
        Assert.True(concrete.Count == 0, string.Join(Environment.NewLine, concrete));
    }

    /// <summary>
    /// The namespaces that hold the view-models and services (IDE-ARCH-081 names ViewModels and Services; the dialog,
    /// document and project view-models and services live in their own namespaces since I-035/I-036/I-065).
    /// </summary>
    private static readonly string[] UiThreadNamespaces =
    [
        "FreexComp.Ide.Core.ViewModels", "FreexComp.Ide.Core.Services", "FreexComp.Ide.Core.Dialogs",
        "FreexComp.Ide.Core.Documents", "FreexComp.Ide.Core.Projects",
    ];

    /// <summary>Blocking waits (IDE-ARCH-081): declaring type and member.</summary>
    private static readonly (string Type, string Member)[] BlockingMembers =
    [
        ("System.Threading.Tasks.Task", "Wait"),
        ("System.Threading.Tasks.Task", "WaitAll"),
        ("System.Threading.Tasks.Task", "WaitAny"),
        ("System.Threading.Tasks.Task`1", "get_Result"),
        ("System.Threading.Tasks.ValueTask`1", "get_Result"),
        ("System.Threading.Thread", "Sleep"),
        ("System.Diagnostics.Process", "WaitForExit"), // synchronous process waits
        // .GetAwaiter().GetResult() outside the compiler's own async state machines
        ("System.Runtime.CompilerServices.TaskAwaiter", "GetResult"),
        ("System.Runtime.CompilerServices.TaskAwaiter`1", "GetResult"),
        ("System.Runtime.CompilerServices.ValueTaskAwaiter`1", "GetResult"),
    ];

    [Fact]
    [Trait("Phase", "1")]
    [Trait("Req", "IDE-ARCH-081")]
    public void NoBlockingCallsInViewModels()
    {
        var core = typeof(Composition.IdeServices).Assembly.Location;
        var references = IlReferences.Read(core);
        Assert.NotEmpty(references); // the scanner sees the code at all
        var stateMachines = AsyncStateMachines(references);

        var blocking = references
            .Where(r => UiThreadNamespaces.Any(ns => r.UserNamespace == ns || r.UserNamespace.StartsWith(ns + ".", StringComparison.Ordinal)))
            .Where(r => BlockingMembers.Contains((r.TargetType, r.Member ?? "")))
            .Where(r => !(r.Member == "GetResult" && stateMachines.Contains(r.UserType)))
            .Select(r => $"{r.UserType} calls {r.TargetType}.{r.Member}")
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();
        Assert.True(blocking.Count == 0, string.Join(Environment.NewLine, blocking));
    }

    /// <summary>The scanner itself finds a blocking call and a network type (guards against a test that sees nothing).</summary>
    [Fact]
    [Trait("Phase", "1")]
    [Trait("Req", "IDE-ARCH-081")]
    [Trait("Req", "IDE-ARCH-093")]
    public void ScannerFindsBlockingCallsAndNetworkTypes()
    {
        var references = IlReferences.Read(typeof(ArchitectureTests).Assembly.Location);
        var mine = references.Where(r => r.UserType == typeof(ScannerSamples).FullName).ToList();
        Assert.Contains(mine, r => r is { TargetType: "System.Threading.Tasks.Task`1", Member: "get_Result" });
        Assert.Contains(mine, r => r is { TargetType: "System.Threading.Thread", Member: "Sleep" });
        Assert.Contains(mine, r => r.TargetType == "System.Net.Sockets.Socket"); // field type
        Assert.Contains(mine, r => r.TargetType == "System.Net.Http.HttpClient"); // generic argument of a field type
        var awaiting = Assert.Single(references, r => r.UserType.StartsWith(typeof(ScannerSamples).FullName + "+<", StringComparison.Ordinal)
                                                     && r is { TargetType: "System.Runtime.CompilerServices.TaskAwaiter`1", Member: "GetResult" });
        Assert.Contains(awaiting.UserType, AsyncStateMachines(references)); // allowed: runs after completion
    }

    /// <summary>The compiler's async state machines (their awaiter GetResult runs after completion, never blocks).</summary>
    private static HashSet<string> AsyncStateMachines(IEnumerable<IlReference> references) =>
        [.. references.Where(r => r is { TargetType: "System.Runtime.CompilerServices.IAsyncStateMachine", Member: null }).Select(r => r.UserType)];

    private static List<string> UiReferences(Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(n => n.StartsWith("Avalonia", StringComparison.Ordinal)
                     || n.StartsWith("Dock", StringComparison.Ordinal))
            .ToList();

#pragma warning disable CA1822, CS0169, CS0649, IDE0051 // scanned, never run
    private sealed class ScannerSamples
    {
        private System.Net.Sockets.Socket? _socket;
        private List<System.Net.Http.HttpClient>? _clients;

        public static Action<int> Sleeper => Thread.Sleep; // a reference to Sleep (ldftn), not a call: tests never sleep

        public static int Blocking(Task<int> task) => task.Result;

        public static async Task<int> Awaiting(Task<int> task) => await task;
    }
#pragma warning restore CA1822, CS0169, CS0649, IDE0051
}
