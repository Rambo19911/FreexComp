using System.Text.RegularExpressions;

namespace FreexComp.Rtl.Tests.Harness;

/// <summary>
/// The target-dependent part of the run-time library (platform-layer.md, RTL-970 ... RTL-976): the inventory of
/// processor- and pointer-width-dependent routines stays true to the sources, the baseline target has no folder of its
/// own, and the harness names the baseline target on every compiler command line.
/// </summary>
[Trait("Phase", "8")]
public sealed partial class PlatformLayerTests
{
    /// <summary>One inventory row: the group, the source file below <c>rtl/</c> and the routines, variables and types it names.</summary>
    public sealed record Row(string Group, string File, string[] Routines, string[] Variables, string[] Types);

    /// <summary>RTL-974: the inventory of platform-layer.md section 2 (the list is kept here; a new row there needs one here).</summary>
    public static readonly Row[] Inventory =
    [
        new("Start-up and entry", "src/System.pas", ["_Startup", "_InitUnits", "_Halt", "_Halt0"], [], []),
        new("Exceptions", "src/System.pas", ["_Raise", "_ReRaise", "_SehFilter", "_BeginCatch", "_EndCatch", "_UnhandledException"], [], []),
        new("Runtime-error address", "src/System.pas", ["_RunError", "_RangeError", "_OverflowError", "_DivZeroError", "_InvalidCast"], [], []),
        new("Memory helpers", "src/System.pas", ["memcpy", "memmove", "memset", "memcmp", "strlen", "wcslen"], ["_fltused"], []),
        new("Thread support", "src/System.pas", ["BeginThread", "EndThread", "_TlsBase", "_TlsRegister"], [], ["TThreadBlock"]),
        new("Floating point", "src/FloatKernels.inc", [], [], []),
        new("Layout records", "src/AbiLayout.inc", [], [],
            ["TStrRec", "TDynArrayRec", "TDynArrayTypeInfo", "TRecordTypeInfo", "TManagedField", "TArrayTypeInfo", "TVmtHeader", "TIntfEntry", "TTextRec"]),
        new("Win32 bindings", "winapi/src/Windows.pas", [], [], []),
    ];

    [GeneratedRegex(@"\{[^}]*\}|\(\*.*?\*\)|//[^\n]*", RegexOptions.Singleline)]
    private static partial Regex Comment();

    [GeneratedRegex(@"(?m)^\s*(\w+)\s*=\s*(?:packed\s+)?record\b")]
    private static partial Regex RecordDeclaration();

    private static string RtlDirectory => Path.Combine(RtlSetup.RepositoryRoot, "rtl");

    private static string Code(string relative) => Comment().Replace(File.ReadAllText(Path.Combine(RtlDirectory, relative)), " ");

    /// <summary>The names of <paramref name="row"/> that <paramref name="code"/> (comments removed) no longer declares.</summary>
    public static List<string> Missing(Row row, string code)
    {
        var missing = new List<string>();
        missing.AddRange(row.Routines.Where(n => !Regex.IsMatch(code, @"(?mi)^\s*(?:procedure|function)\s+" + n + @"\b")));
        missing.AddRange(row.Variables.Where(n => !Regex.IsMatch(code, @"(?mi)^\s*" + n + @"\s*:")));
        missing.AddRange(row.Types.Where(n => !Regex.IsMatch(code, @"(?mi)^\s*" + n + @"\s*=\s*(?:packed\s+)?record\b")));
        return missing;
    }

    /// <summary>RTL-974: every name of the inventory still exists in the file its row names.</summary>
    [Fact]
    [Trait("Req", "RTL-974")]
    [Trait("Req", "RTL-976")]
    public void InventoryMatchesSources()
    {
        var problems = new List<string>();
        foreach (var row in Inventory)
        {
            var path = Path.Combine(RtlDirectory, row.File);
            if (!File.Exists(path))
            {
                problems.Add($"{row.Group}: {row.File} does not exist");
                continue;
            }

            problems.AddRange(Missing(row, Code(row.File)).Select(n => $"{row.Group}: {n} is not declared in {row.File}"));
        }

        // "all records" of the layout file: a record added there belongs to the inventory row
        var layout = Inventory.Single(r => r.Group == "Layout records");
        problems.AddRange(RecordDeclaration().Matches(Code(layout.File)).Select(m => m.Groups[1].Value)
            .Where(n => !layout.Types.Contains(n, StringComparer.OrdinalIgnoreCase))
            .Select(n => $"Layout records: {n} of {layout.File} is not in the inventory"));

        // floating-point control: the start-up sequence sets the control register (ABI-105)
        var system = Code("src/System.pas");
        var implementation = system[Regex.Match(system, @"(?m)^implementation\b").Index..];
        var startup = Regex.Match(implementation, @"(?ms)^procedure _Startup\b.*?^end;", RegexOptions.None, TimeSpan.FromSeconds(10));
        if (!startup.Value.Contains("_SetMXCSR(", StringComparison.Ordinal))
        {
            problems.Add("Floating point: _Startup no longer sets the control register with _SetMXCSR");
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    [Trait("Req", "RTL-974")]
    public void Inventory_ReportsANameThatIsGone()
    {
        var row = new Row("g", "f", ["_Kept", "_Gone"], ["_var", "_novar"], ["TKept", "TGone"]);
        const string Code = "procedure _Kept(X: Integer);\n  _var: Integer = 1;\n  TKept = record\n  { } function _GoneNot: Integer;\n  TGone = class\n";
        Assert.Equal(["_Gone", "_novar", "TGone"], Missing(row, Code));
    }

    /// <summary>RTL-970 / RTL-972: the baseline target has no source or definition folder of its own.</summary>
    [Fact]
    [Trait("Req", "RTL-970")]
    [Trait("Req", "RTL-972")]
    [Trait("Req", "RTL-976")]
    public void NoTargetFoldersForBaseline()
    {
        Assert.Equal("win64", RtlSetup.BaselineTarget);
        Assert.False(Directory.Exists(Path.Combine(RtlDirectory, "src", RtlSetup.BaselineTarget)), "rtl/src/win64 must not exist (RTL-970)");
        Assert.False(Directory.Exists(Path.Combine(RtlDirectory, "winapi", "def", RtlSetup.BaselineTarget)), "rtl/winapi/def/win64 must not exist (RTL-972)");
        Assert.True(File.Exists(Path.Combine(RtlDirectory, "src", "System.pas")));
        Assert.True(File.Exists(Path.Combine(RtlDirectory, "winapi", "def", "kernel32.def")));
    }

    /// <summary>
    /// RTL-976: every compiler command line of the harness names the baseline target. The golden runner's arguments carry
    /// <c>--target win64</c>, and no harness source builds an RTL command line of its own (<c>--rtl</c> is spelled only
    /// by <see cref="RtlSetup.CompilerOptions"/>).
    /// </summary>
    [Fact]
    [Trait("Req", "RTL-976")]
    public void Harness_PassesBaselineTargetExplicitly()
    {
        var setup = RtlSetup.Default;
        Assert.Equal("win64", setup.Target);
        AssertTargetOption(setup.CompilerOptions, "win64");

        var @case = new RtlCase("infra/Harness_SelfTest", Path.Combine(RtlSetup.TestsDirectory, "infra", "Harness_SelfTest.pas"));
        var header = RtlCorpus.ReadHeader("// opc: -dX\nprogram P;\nbegin\nend.\n");
        var args = GoldenRunner.OpcArguments(@case, header, setup, Path.Combine(Path.GetTempPath(), "rtl-work"));
        AssertTargetOption(args, "win64");
        Assert.True(args.IndexOf("--target") < args.IndexOf("-dX"), "a program's own options come after the target");
        Assert.Equal(1, args.Count(a => a == "--target"));

        var harness = Path.Combine(RtlSetup.TestsDirectory, "Harness");
        var own = Directory.GetFiles(harness, "*.cs")
            .Where(f => Path.GetFileName(f) != "PlatformLayerTests.cs")
            .SelectMany(f => File.ReadAllLines(f).Where(l => l.Contains("\"--rtl\"", StringComparison.Ordinal)).Select(l => Path.GetFileName(f) + ": " + l.Trim()))
            .ToList();
        Assert.True(own.Count == 1 && own[0].StartsWith("GoldenRunner.cs:", StringComparison.Ordinal),
            "only RtlSetup.CompilerOptions spells --rtl (so every command line has --target):\n" + string.Join("\n", own));
    }

    private static void AssertTargetOption(IReadOnlyList<string> args, string target)
    {
        var index = args.ToList().IndexOf("--target");
        Assert.True(index >= 0 && index + 1 < args.Count && args[index + 1] == target, "no '--target " + target + "' in: " + string.Join(" ", args));
    }
}
