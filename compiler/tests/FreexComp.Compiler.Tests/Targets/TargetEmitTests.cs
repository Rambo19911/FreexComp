using FreexComp.Compiler.Emit;
using FreexComp.Compiler.Targets;
using FreexComp.Compiler.Tests.Toolchain;
using static FreexComp.Compiler.Tests.Targets.TargetTestSupport;

namespace FreexComp.Compiler.Tests.Targets;

/// <summary>targets.md CMP-1011…1013 (task C-281): calling conventions, the personality routine and the stack probe are read from the target.</summary>
[Trait("Phase", "8")]
[Trait("Kind", "Unit")]
public sealed class TargetEmitTests
{
    private const string Conventions = """
        program CC;
        type
          TProc = function(X: Integer): Integer; stdcall;
          TPlainProc = function(X: Integer): Integer;
          TShape = class
            function Area(X: Integer): Integer; virtual; stdcall;
          end;
        function Ext(X: Integer): Integer; stdcall; external 'user32.dll' name 'Ext';
        function Std(X: Integer): Integer; stdcall;
        begin
          Result := X;
        end;
        function Plain(X: Integer): Integer;
        begin
          Result := X;
        end;
        function TShape.Area(X: Integer): Integer;
        begin
          Result := X;
        end;
        var
          p: TProc;
          q: TPlainProc;
          s: TShape;
          n: Integer;
        begin
          n := Std(1);
          n := Ext(2);
          p := Std;
          n := p(3);
          n := Plain(4);
          q := Plain;
          n := q(5);
          n := s.Area(6);
          try
            n := Std(7);
          except
            n := 0;
          end;
        end.
        """;

    [Fact]
    [Trait("Req", "CMP-1011")]
    public void CallConvKeywordFromTable()
    {
        var target = TargetInfo.Win64 with
        {
            Id = "cc",
            CallingConventions = TargetInfo.Win64.CallingConventions.SetItem(CallConvDirective.StdCall, new CallConvInfo("x86_stdcallcc", SymbolDecoration.None)),
        };

        var ll = EmitLl(target, "CC", Conventions)["CC"];

        // define and declare: the keyword in the calling-convention position, before the return type
        Assert.Contains("define internal x86_stdcallcc i32 @\"CC.Std\"(i32 %X) #0", ll, StringComparison.Ordinal);
        Assert.Contains("define internal x86_stdcallcc i32 @\"CC.TShape.Area\"(ptr %Self, i32 %X) #0", ll, StringComparison.Ordinal);
        Assert.Contains("declare dllimport x86_stdcallcc i32 @Ext(i32)\n", ll, StringComparison.Ordinal);
        // call: a direct call, an imported routine, a call through a procedural value of a stdcall type, a virtual call
        Assert.Contains("call x86_stdcallcc i32 @\"CC.Std\"(i32 1)\n", ll, StringComparison.Ordinal);
        Assert.Contains("call x86_stdcallcc i32 @Ext(i32 2)\n", ll, StringComparison.Ordinal);
        Assert.Matches("call x86_stdcallcc i32 %t[0-9]+\\(i32 3\\)\n", ll);
        Assert.Matches("call x86_stdcallcc i32 %t[0-9]+\\(ptr %t[0-9]+, i32 6\\)\n", ll);
        // invoke: inside the try part
        Assert.Contains("invoke x86_stdcallcc i32 @\"CC.Std\"(i32 7)\n", ll, StringComparison.Ordinal);
        // none for Default: the routine without a directive and the procedural type without one
        Assert.Contains("define internal i32 @\"CC.Plain\"(i32 %X) #0", ll, StringComparison.Ordinal);
        Assert.Contains("call i32 @\"CC.Plain\"(i32 4)\n", ll, StringComparison.Ordinal);
        Assert.Matches("call i32 %t[0-9]+\\(i32 5\\)\n", ll);
        Assert.Equal(8, Lines(ll).Count(l => l.Contains("x86_stdcallcc", StringComparison.Ordinal)));

        // the baseline target maps every directive to the default convention: nothing is printed
        var win64 = EmitLl(TargetInfo.Win64, "CC", Conventions)["CC"];
        Assert.DoesNotContain("cc ", win64.Replace("ccc", "", StringComparison.Ordinal).Replace("@\"CC.", "", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("define internal i32 @\"CC.Std\"(i32 %X) #0", win64, StringComparison.Ordinal);
        Assert.Contains("declare dllimport i32 @Ext(i32)\n", win64, StringComparison.Ordinal);
        Assert.Contains("call i32 @\"CC.Std\"(i32 1)\n", win64, StringComparison.Ordinal);

        // the directive is part of a unit's interface: it reaches the caller through the .pcu of a used unit
        const string Unit = "unit U; interface function Std(X: Integer): Integer; stdcall; implementation function Std(X: Integer): Integer; begin Result := X; end; end.";
        const string User = "program P; uses U; var n: Integer; begin n := Std(1); end.";
        const string Bin = @"C:\llvm\bin";
        var fs = new InMemoryFileSystem().Add(Path.Combine(Rtl, "System.pas"), StubSystem).Add(@"C:\p\U.pas", Unit).Add(@"C:\p\P.pas", User);
        foreach (var tool in new[] { "clang.exe", "lld-link.exe", "llvm-dlltool.exe" })
        {
            fs.Add(Path.Combine(Bin, tool), "");
        }

        // the stand-in for clang "writes" the object file, so the .pcu / .obj pair of U is complete after the first run
        var runner = new FakeProcessRunner((call, _) =>
        {
            if (call.Arguments is ["--version"])
            {
                return Task.FromResult((0, new[] { "clang version " + FreexComp.Compiler.Toolchain.ToolchainVersionCheck.PinnedVersion }, Array.Empty<string>()));
            }

            fs.Add(call.Arguments[3], "obj");
            return Task.FromResult((0, Array.Empty<string>(), Array.Empty<string>()));
        });
        var options = new CompilationOptions { MainFile = @"C:\p\P.pas", RtlDirectory = Rtl, ObjectDirectory = Obj, ToolchainDirectory = Bin, Target = target };
        var emit = new EmitOptions { Stage = EmitStage.Obj, ProcessRunner = runner };
        var first = Compilation.Create(options, [], fs).Emit(emit, TestContext.Current.CancellationToken);
        Assert.True(first.Success, string.Join("\n", first.Diagnostics.Select(d => d.ToText())));
        Assert.Contains("\"cc:StdCall\"", fs.ReadAllText(Path.Combine(Obj, "U.pcu")), StringComparison.Ordinal);
        fs.Add(@"C:\p\P.pas", User.Replace("Std(1)", "Std(2)", StringComparison.Ordinal));          // U is now taken from its .pcu
        var second = Compilation.Create(options, [], fs).Emit(emit, TestContext.Current.CancellationToken);
        Assert.True(second.Success, string.Join("\n", second.Diagnostics.Select(d => d.ToText())));
        Assert.Equal(["P"], second.CompiledUnits);
        var user = fs.ReadAllText(Path.Combine(Obj, "P.ll"));
        Assert.Contains("declare x86_stdcallcc i32 @\"U.Std\"(i32)\n", user, StringComparison.Ordinal);
        Assert.Contains("call x86_stdcallcc i32 @\"U.Std\"(i32 2)\n", user, StringComparison.Ordinal);
    }

    private const string Handlers = """
        program EH;
        procedure MayRaise; begin end;
        begin
          try
            MayRaise;
          except
          end;
        end.
        """;

    [Fact]
    [Trait("Req", "CMP-1012")]
    public async Task PersonalityAndImportLibraryFromTarget()
    {
        var target = TargetInfo.Win64 with { Id = "eh", PersonalitySymbol = "__fake_personality", PersonalityLibrary = "fakeseh.dll" };

        var ll = EmitLl(target, "EH", Handlers)["EH"];
        Assert.Contains(" personality ptr @__fake_personality {\n", ll, StringComparison.Ordinal);
        Assert.Contains("declare i32 @__fake_personality(...)\n", ll, StringComparison.Ordinal);
        Assert.DoesNotContain(TargetInfo.Win64.PersonalitySymbol, ll, StringComparison.Ordinal);
        var win64 = EmitLl(TargetInfo.Win64, "EH", Handlers)["EH"];
        Assert.Contains(" personality ptr @__C_specific_handler {\n", win64, StringComparison.Ordinal);
        Assert.Contains("declare i32 @__C_specific_handler(...)\n", win64, StringComparison.Ordinal);

        // CMP-620: the personality routine's DLL is always linked, in place of the baseline's
        Assert.Equal(["kernel32.dll", "fakeseh.dll"], LinkInputs.AlwaysLinked(target));
        Assert.Equal(["kernel32.dll", "ntdll.dll"], LinkInputs.AlwaysLinked(TargetInfo.Win64));

        // … and its import library is built and linked: the link of a program for the fake target
        const string Bin = @"C:\llvm\bin";
        var fs = new InMemoryFileSystem().Add(Path.Combine(Rtl, "System.pas"), StubSystem).Add(@"C:\p\EH.pas", Handlers)
            .Add(@"C:\winapi\def\kernel32.def", "LIBRARY kernel32.dll\nEXPORTS\nExitProcess\n")
            .Add(@"C:\winapi\def\fakeseh.def", "LIBRARY fakeseh.dll\nEXPORTS\n__fake_personality\n")
            .Add(@"C:\winapi\def\ntdll.def", "LIBRARY ntdll.dll\nEXPORTS\n__C_specific_handler\n");
        foreach (var tool in new[] { "clang.exe", "lld-link.exe", "llvm-dlltool.exe" })
        {
            fs.Add(Path.Combine(Bin, tool), "");
        }

        var runner = new FakeProcessRunner((call, _) => Task.FromResult(call.Arguments is ["--version"]
            ? (0, new[] { "clang version " + FreexComp.Compiler.Toolchain.ToolchainVersionCheck.PinnedVersion }, Array.Empty<string>())
            : (0, Array.Empty<string>(), Array.Empty<string>())));
        var options = new CompilationOptions { MainFile = @"C:\p\EH.pas", RtlDirectory = Rtl, ObjectDirectory = Obj, ToolchainDirectory = Bin, Target = target };
        var result = await Compilation.Create(options, [], fs)
            .EmitAsync(new EmitOptions { Stage = EmitStage.Exe, OutputPath = @"C:\p\EH.exe", ProcessRunner = runner }, TestContext.Current.CancellationToken);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.ToText())));

        var libraries = runner.Calls.Where(c => c.FileName.EndsWith("llvm-dlltool.exe", StringComparison.Ordinal)).Select(c => c.Arguments[5]).Order(StringComparer.Ordinal);
        Assert.Equal(["fakeseh.dll", "kernel32.dll"], libraries);
        var rsp = fs.ReadAllText(Path.Combine(Obj, "link.rsp"));
        Assert.Contains(Path.Combine(Obj, "imports", "fakeseh.lib") + "\n", rsp, StringComparison.Ordinal);
        Assert.DoesNotContain("ntdll", rsp, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "CMP-1013")]
    public void StackProbeAssemblyFromTarget()
    {
        var target = TargetInfo.Win64 with
        {
            Id = "probe",
            StackProbeSymbol = "__fakeprobe",
            StackProbeAssembly = ["\t.text", "\t.globl\t__fakeprobe", "__fakeprobe:", "\tretq"],
        };
        const string Program = "program P; begin end.";

        var fake = EmitLl(target, "P", Program);
        // unit System carries the target's lines, in order, right after the module header; no other module has module assembly
        Assert.StartsWith(
            "; ModuleID = 'System'\nsource_filename = \"System.pas\"\ntarget triple = \"x86_64-pc-windows-msvc\"\n\n"
            + "module asm \"\\09.text\"\nmodule asm \"\\09.globl\\09__fakeprobe\"\nmodule asm \"__fakeprobe:\"\nmodule asm \"\\09retq\"\n",
            fake["System"], StringComparison.Ordinal);
        Assert.Equal(4, Lines(fake["System"]).Count(l => l.StartsWith("module asm ", StringComparison.Ordinal)));
        Assert.DoesNotContain("__chkstk", fake["System"], StringComparison.Ordinal);
        Assert.DoesNotContain("module asm", fake["P"], StringComparison.Ordinal);

        // the baseline target: the 21 lines of toolchain.md CMP-611, character for character
        var win64 = EmitLl(TargetInfo.Win64, "P", Program);
        var asm = Lines(win64["System"]).Where(l => l.StartsWith("module asm ", StringComparison.Ordinal)).ToArray();
        string[] expected =
        [
            "module asm \"\\09.text\"", "module asm \"\\09.globl\\09__chkstk\"", "module asm \"\\09.p2align\\094\"", "module asm \"__chkstk:\"",
            "module asm \"\\09pushq\\09%rcx\"", "module asm \"\\09pushq\\09%rax\"", "module asm \"\\09cmpq\\09$4096, %rax\"",
            "module asm \"\\09leaq\\0924(%rsp), %rcx\"", "module asm \"\\09jb\\091f\"", "module asm \"2:\"", "module asm \"\\09subq\\09$4096, %rcx\"",
            "module asm \"\\09testq\\09%rcx, (%rcx)\"", "module asm \"\\09subq\\09$4096, %rax\"", "module asm \"\\09cmpq\\09$4096, %rax\"",
            "module asm \"\\09ja\\092b\"", "module asm \"1:\"", "module asm \"\\09subq\\09%rax, %rcx\"", "module asm \"\\09testq\\09%rcx, (%rcx)\"",
            "module asm \"\\09popq\\09%rax\"", "module asm \"\\09popq\\09%rcx\"", "module asm \"\\09retq\"",
        ];
        Assert.Equal(expected, asm);
        Assert.DoesNotContain("module asm", win64["P"], StringComparison.Ordinal);
        Assert.Equal(4096, TargetInfo.Win64.StackProbeThreshold);
        Assert.Contains(TargetInfo.Win64.StackProbeSymbol + ":", TargetInfo.Win64.StackProbeAssembly);
    }
}
