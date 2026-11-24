// the target descriptors are read all over the compiler
global using FreexComp.Compiler.Targets;

using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace FreexComp.Compiler.Targets;

/// <summary>The calling-convention directive written on a routine, method, procedural type or <c>external</c> declaration (CMP-1011).</summary>
public enum CallConvDirective { Default, Register, Pascal, Cdecl, StdCall, SafeCall, WinApi }

/// <summary>How the mangler decorates <c>external</c> import names and export names of a calling convention (CMP-1011).</summary>
public enum SymbolDecoration { None, LeadingUnderscore, StdCallBytes, FastCallBytes }

/// <summary>What a calling-convention directive means for a target: the LLVM keyword ("" = the default convention, not printed) and the symbol decoration.</summary>
public sealed record CallConvInfo(string LlvmKeyword, SymbolDecoration Decoration);

/// <summary>The exception-handling lowering of a target (CMP-1012).</summary>
public enum ExceptionModel { SehTable }

/// <summary>
/// A compilation target (targets.md §2, CMP-1002): one immutable value holding every fact about the machine, the object format
/// flavour, the tool command lines and the RTL lookup that differs between targets. Nothing outside this folder states such a
/// fact as a literal (CMP-1000).
/// </summary>
public sealed record TargetInfo
{
    // identity

    /// <summary>The target id used in project files and on command lines; <c>^[a-z][a-z0-9]{1,15}$</c>.</summary>
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    // machine

    public required string Triple { get; init; }

    /// <summary>Reference copy of the back end's data layout string for <see cref="Triple"/> (CMP-1006); never written to a module.</summary>
    public required string DataLayout { get; init; }

    /// <summary>Bytes.</summary>
    public required int PointerSize { get; init; }

    public required int PointerAlign { get; init; }

    public required int Int64Align { get; init; }

    public required int Float64Align { get; init; }

    public required int StackAlign { get; init; }

    /// <summary>Frames larger than this many bytes rely on <see cref="StackProbeSymbol"/> (CMP-1013).</summary>
    public required int StackProbeThreshold { get; init; }

    // code generation

    public required ImmutableDictionary<CallConvDirective, CallConvInfo> CallingConventions { get; init; }

    public required ExceptionModel ExceptionModel { get; init; }

    public required string PersonalitySymbol { get; init; }

    /// <summary>The DLL that exports <see cref="PersonalitySymbol"/>; always part of the link (CMP-1012).</summary>
    public required string PersonalityLibrary { get; init; }

    public required string StackProbeSymbol { get; init; }

    /// <summary>The module-level assembly lines appended to unit <c>System</c> (CMP-1013).</summary>
    public required ImmutableArray<string> StackProbeAssembly { get; init; }

    /// <summary>The target group of predefined conditional symbols, in definition order (CMP-1010).</summary>
    public required ImmutableArray<string> PredefinedSymbols { get; init; }

    // tools

    public required ImmutableArray<string> ClangArguments { get; init; }

    public required string DllToolMachine { get; init; }

    public required string LinkerMachine { get; init; }

    /// <summary>Extra linker lines, written directly after <c>/nxcompat</c> (CMP-1016).</summary>
    public required ImmutableArray<string> LinkerArguments { get; init; }

    public required string EntrySymbol { get; init; }

    public required string LibraryEntrySymbol { get; init; }

    /// <summary>The COFF machine value of objects and images.</summary>
    public required int CoffMachine { get; init; }

    // RTL lookup

    /// <summary>Sub-directory of the RTL root searched before the root itself (CMP-1022).</summary>
    public required string RtlSubdirectory { get; init; }

    /// <summary>Sub-directory of the import-definition folder searched before the flat folder (CMP-1023).</summary>
    public required string ImportDefSubdirectory { get; init; }

    /// <summary>
    /// CMP-1005: <c>&lt;Id&gt;;sha256:&lt;hex&gt;</c> over every member except <see cref="Id"/> and <see cref="DisplayName"/>, in
    /// declaration order (strings as UTF-8, numbers invariant, arrays and the dictionary in declaration / enum order, separated by
    /// line feeds).
    /// </summary>
    public string Fingerprint
    {
        get
        {
            var text = new StringBuilder();
            void Line(string value) => text.Append(value).Append('\n');
            void Number(int value) => Line(value.ToString(CultureInfo.InvariantCulture));
            void Lines(ImmutableArray<string> values)
            {
                Number(values.Length);
                foreach (var value in values)
                {
                    Line(value);
                }
            }

            Line(Triple);
            Line(DataLayout);
            Number(PointerSize);
            Number(PointerAlign);
            Number(Int64Align);
            Number(Float64Align);
            Number(StackAlign);
            Number(StackProbeThreshold);
            foreach (var directive in Enum.GetValues<CallConvDirective>())
            {
                var info = CallingConventions.GetValueOrDefault(directive);
                Line(directive + "=" + (info is null ? "" : info.LlvmKeyword + "," + info.Decoration));
            }

            Line(ExceptionModel.ToString());
            Line(PersonalitySymbol);
            Line(PersonalityLibrary);
            Line(StackProbeSymbol);
            Lines(StackProbeAssembly);
            Lines(PredefinedSymbols);
            Lines(ClangArguments);
            Line(DllToolMachine);
            Line(LinkerMachine);
            Lines(LinkerArguments);
            Line(EntrySymbol);
            Line(LibraryEntrySymbol);
            Number(CoffMachine);
            Line(RtlSubdirectory);
            Line(ImportDefSubdirectory);
            return Id + ";sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
        }
    }

    /// <summary>The baseline target: Windows x64.</summary>
    public static TargetInfo Win64 { get; } = new()
    {
        Id = "win64",
        DisplayName = "Windows 64-bit",
        Triple = "x86_64-pc-windows-msvc",
        DataLayout = "e-m:w-p270:32:32-p271:32:32-p272:64:64-i64:64-i128:128-f80:128-n8:16:32:64-S128",
        PointerSize = 8,
        PointerAlign = 8,
        Int64Align = 8,
        Float64Align = 8,
        StackAlign = 16,
        StackProbeThreshold = 4096,
        CallingConventions = Enum.GetValues<CallConvDirective>().ToImmutableDictionary(d => d, _ => new CallConvInfo("", SymbolDecoration.None)),
        ExceptionModel = ExceptionModel.SehTable,
        PersonalitySymbol = "__C_specific_handler",
        PersonalityLibrary = "ntdll.dll",
        StackProbeSymbol = "__chkstk",
        StackProbeAssembly =
        [
            "\t.text", "\t.globl\t__chkstk", "\t.p2align\t4", "__chkstk:", "\tpushq\t%rcx", "\tpushq\t%rax", "\tcmpq\t$4096, %rax",
            "\tleaq\t24(%rsp), %rcx", "\tjb\t1f", "2:", "\tsubq\t$4096, %rcx", "\ttestq\t%rcx, (%rcx)", "\tsubq\t$4096, %rax",
            "\tcmpq\t$4096, %rax", "\tja\t2b", "1:", "\tsubq\t%rax, %rcx", "\ttestq\t%rcx, (%rcx)", "\tpopq\t%rax", "\tpopq\t%rcx", "\tretq",
        ],
        PredefinedSymbols = ["MSWINDOWS", "WINDOWS", "WIN64", "CPUX64", "CPU64BITS"],
        ClangArguments = ["--target=x86_64-pc-windows-msvc"],
        DllToolMachine = "i386:x86-64",
        LinkerMachine = "x64",
        LinkerArguments = [],
        EntrySymbol = "_start",
        LibraryEntrySymbol = "_dllstart",
        CoffMachine = 0x8664,
        RtlSubdirectory = "win64",
        ImportDefSubdirectory = "win64",
    };
}
