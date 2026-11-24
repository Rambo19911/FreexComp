using System.Collections.Immutable;
using System.Reflection;
using System.Text.RegularExpressions;
using FreexComp.Compiler.Targets;
using FreexComp.Compiler.Units;

namespace FreexComp.Compiler.Tests.Targets;

/// <summary>targets.md §2 (task C-278): the descriptor, the registry and the fingerprint (CMP-1002, 1004, 1005).</summary>
[Trait("Phase", "8")]
[Trait("Kind", "Unit")]
public sealed class TargetInfoTests
{
    [Fact]
    [Trait("Req", "CMP-1002")]
    public void Win64Descriptor_HasSpecValues()
    {
        var t = TargetInfo.Win64;

        Assert.Equal("win64", t.Id);
        Assert.Matches("^[a-z][a-z0-9]{1,15}$", t.Id);
        Assert.Equal("Windows 64-bit", t.DisplayName);
        Assert.Equal("x86_64-pc-windows-msvc", t.Triple);
        Assert.Equal("e-m:w-p270:32:32-p271:32:32-p272:64:64-i64:64-i128:128-f80:128-n8:16:32:64-S128", t.DataLayout);
        Assert.Equal(8, t.PointerSize);
        Assert.Equal(8, t.PointerAlign);
        Assert.Equal(8, t.Int64Align);
        Assert.Equal(8, t.Float64Align);
        Assert.Equal(16, t.StackAlign);
        Assert.Equal(4096, t.StackProbeThreshold);
        // ABI-080: one convention on x64 — every directive maps to the default convention, undecorated
        Assert.Equal(Enum.GetValues<CallConvDirective>().Order(), t.CallingConventions.Keys.Order());
        Assert.All(t.CallingConventions.Values, info => Assert.Equal(new CallConvInfo("", SymbolDecoration.None), info));
        Assert.Equal(ExceptionModel.SehTable, t.ExceptionModel);
        Assert.Equal("__C_specific_handler", t.PersonalitySymbol);
        Assert.Equal("ntdll.dll", t.PersonalityLibrary);
        Assert.Equal("__chkstk", t.StackProbeSymbol);
        // the module-asm lines of toolchain.md CMP-611, character for character
        Assert.Equal(
            [
                "\t.text", "\t.globl\t__chkstk", "\t.p2align\t4", "__chkstk:", "\tpushq\t%rcx", "\tpushq\t%rax", "\tcmpq\t$4096, %rax",
                "\tleaq\t24(%rsp), %rcx", "\tjb\t1f", "2:", "\tsubq\t$4096, %rcx", "\ttestq\t%rcx, (%rcx)", "\tsubq\t$4096, %rax",
                "\tcmpq\t$4096, %rax", "\tja\t2b", "1:", "\tsubq\t%rax, %rcx", "\ttestq\t%rcx, (%rcx)", "\tpopq\t%rax", "\tpopq\t%rcx", "\tretq",
            ],
            t.StackProbeAssembly);
        Assert.Equal(["MSWINDOWS", "WINDOWS", "WIN64", "CPUX64", "CPU64BITS"], t.PredefinedSymbols);
        Assert.Equal(["--target=x86_64-pc-windows-msvc"], t.ClangArguments);
        Assert.Equal("i386:x86-64", t.DllToolMachine);
        Assert.Equal("x64", t.LinkerMachine);
        Assert.Empty(t.LinkerArguments);
        Assert.Equal("_start", t.EntrySymbol);
        Assert.Equal("_dllstart", t.LibraryEntrySymbol);
        Assert.Equal(0x8664, t.CoffMachine);
        Assert.Equal("win64", t.RtlSubdirectory);
        Assert.Equal("win64", t.ImportDefSubdirectory);
        Assert.Matches("^win64;sha256:[0-9a-f]{64}$", t.Fingerprint);
    }

    [Fact]
    [Trait("Req", "CMP-1003")]
    [Trait("Req", "CMP-1004")]
    public void Registry_ContainsOnlyWin64_DefaultIsWin64()
    {
        Assert.Same(TargetInfo.Win64, Assert.Single(TargetRegistry.BuiltIn));
        Assert.Same(TargetInfo.Win64, TargetRegistry.Default);
        Assert.True(TargetRegistry.TryGet("win64", out var found));
        Assert.Same(TargetInfo.Win64, found);
        Assert.False(TargetRegistry.TryGet("Win64", out _));                // ordinal, case-sensitive
        Assert.False(TargetRegistry.TryGet("win32", out _));
        Assert.False(TargetRegistry.TryGet("", out _));

        // closed and immutable: no way to add a target, no settable state
        Assert.DoesNotContain(typeof(TargetRegistry).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static),
            m => m.Name.Contains("Register", StringComparison.OrdinalIgnoreCase) || m.Name.Contains("Add", StringComparison.OrdinalIgnoreCase));
        Assert.All(typeof(TargetRegistry).GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static), p => Assert.Null(p.SetMethod));
        Assert.All(typeof(TargetRegistry).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static), f => Assert.True(f.IsInitOnly));

        // the target of a compilation defaults to the registry's default
        Assert.Same(TargetRegistry.Default, new CompilationOptions { MainFile = @"C:\p\P.pas" }.Target);
    }

    /// <summary>One <c>with</c> per member that is part of the fingerprint.</summary>
    private static readonly ImmutableDictionary<string, TargetInfo> Variants = new Dictionary<string, TargetInfo>
    {
        [nameof(TargetInfo.Triple)] = TargetInfo.Win64 with { Triple = "i686-pc-windows-msvc" },
        [nameof(TargetInfo.DataLayout)] = TargetInfo.Win64 with { DataLayout = "e-m:x-p:32:32" },
        [nameof(TargetInfo.PointerSize)] = TargetInfo.Win64 with { PointerSize = 4 },
        [nameof(TargetInfo.PointerAlign)] = TargetInfo.Win64 with { PointerAlign = 4 },
        [nameof(TargetInfo.Int64Align)] = TargetInfo.Win64 with { Int64Align = 4 },
        [nameof(TargetInfo.Float64Align)] = TargetInfo.Win64 with { Float64Align = 4 },
        [nameof(TargetInfo.StackAlign)] = TargetInfo.Win64 with { StackAlign = 4 },
        [nameof(TargetInfo.StackProbeThreshold)] = TargetInfo.Win64 with { StackProbeThreshold = 8192 },
        [nameof(TargetInfo.CallingConventions)] = TargetInfo.Win64 with
        {
            CallingConventions = TargetInfo.Win64.CallingConventions.SetItem(CallConvDirective.StdCall, new CallConvInfo("x86_stdcallcc", SymbolDecoration.StdCallBytes)),
        },
        [nameof(TargetInfo.ExceptionModel)] = TargetInfo.Win64 with { ExceptionModel = (ExceptionModel)1 },
        [nameof(TargetInfo.PersonalitySymbol)] = TargetInfo.Win64 with { PersonalitySymbol = "_except_handler" },
        [nameof(TargetInfo.PersonalityLibrary)] = TargetInfo.Win64 with { PersonalityLibrary = "other.dll" },
        [nameof(TargetInfo.StackProbeSymbol)] = TargetInfo.Win64 with { StackProbeSymbol = "_probe" },
        [nameof(TargetInfo.StackProbeAssembly)] = TargetInfo.Win64 with { StackProbeAssembly = TargetInfo.Win64.StackProbeAssembly.Add("\tnop") },
        [nameof(TargetInfo.PredefinedSymbols)] = TargetInfo.Win64 with { PredefinedSymbols = TargetInfo.Win64.PredefinedSymbols.Add("EXTRA") },
        [nameof(TargetInfo.ClangArguments)] = TargetInfo.Win64 with { ClangArguments = TargetInfo.Win64.ClangArguments.Add("-mextra") },
        [nameof(TargetInfo.DllToolMachine)] = TargetInfo.Win64 with { DllToolMachine = "i386" },
        [nameof(TargetInfo.LinkerMachine)] = TargetInfo.Win64 with { LinkerMachine = "x86" },
        [nameof(TargetInfo.LinkerArguments)] = TargetInfo.Win64 with { LinkerArguments = ["/safeseh:no"] },
        [nameof(TargetInfo.EntrySymbol)] = TargetInfo.Win64 with { EntrySymbol = "_begin" },
        [nameof(TargetInfo.LibraryEntrySymbol)] = TargetInfo.Win64 with { LibraryEntrySymbol = "_dllbegin" },
        [nameof(TargetInfo.CoffMachine)] = TargetInfo.Win64 with { CoffMachine = 0x14c },
        [nameof(TargetInfo.RtlSubdirectory)] = TargetInfo.Win64 with { RtlSubdirectory = "other" },
        [nameof(TargetInfo.ImportDefSubdirectory)] = TargetInfo.Win64 with { ImportDefSubdirectory = "other" },
    }.ToImmutableDictionary();

    [Fact]
    [Trait("Req", "CMP-1005")]
    public void Fingerprint_ChangesWithEveryCodeAffectingMember()
    {
        var baseline = TargetInfo.Win64.Fingerprint;

        // every member of the record except Id, DisplayName and Fingerprint has its variant here: a new member cannot be forgotten
        var members = typeof(TargetInfo).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name)
            .Except([nameof(TargetInfo.Id), nameof(TargetInfo.DisplayName), nameof(TargetInfo.Fingerprint)]).Order(StringComparer.Ordinal);
        Assert.Equal(members, Variants.Keys.Order(StringComparer.Ordinal));

        var seen = new HashSet<string>(StringComparer.Ordinal) { baseline };
        foreach (var (member, variant) in Variants.OrderBy(v => v.Key, StringComparer.Ordinal))
        {
            Assert.Matches("^win64;sha256:[0-9a-f]{64}$", variant.Fingerprint);
            Assert.True(seen.Add(variant.Fingerprint), $"changing {member} does not give a fingerprint of its own");
        }

        // Id is the readable prefix, not part of the hash; DisplayName does not affect code
        var renamed = TargetInfo.Win64 with { Id = "other" };
        Assert.Equal("other" + baseline["win64".Length..], renamed.Fingerprint);
        Assert.Equal(baseline, (TargetInfo.Win64 with { DisplayName = "Something else" }).Fingerprint);
        Assert.Equal(baseline, (TargetInfo.Win64 with { }).Fingerprint);                         // deterministic for an equal descriptor

        // CMP-150 rule 3: the options fingerprint holds the target fingerprint, so any descriptor change recompiles everything
        var options = new CompilationOptions { MainFile = @"C:\p\P.pas" };
        var standard = Fingerprint.Options(options, "aliases");
        Assert.Equal(standard, Fingerprint.Options(options with { Target = TargetInfo.Win64 with { DisplayName = "x" } }, "aliases"));
        Assert.NotEqual(standard, Fingerprint.Options(options with { Target = renamed }, "aliases"));
        Assert.All(Variants.Values, v => Assert.NotEqual(standard, Fingerprint.Options(options with { Target = v }, "aliases")));
        Assert.Matches(new Regex("^sha256:[0-9a-f]{64}$"), standard);
    }
}
