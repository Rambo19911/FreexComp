namespace FreexComp.Compiler.Tests.Emit;

/// <summary>
/// dll-output.md §3 (C-287): the library module — <c>$units</c>/<c>$main</c> without <c>_start</c>, the entry routine
/// (CMP-1050…1052) and the export thunks (CMP-1053, 1054).
/// </summary>
[Trait("Phase", "8")]
public sealed class ExportThunkTests
{
    private const string Dir = @"C:\p";

    /// <summary>The example of dll-output.md §2.3 with a library block, a routine of a used unit and a managed global.</summary>
    internal const string MathLibSource = """
        library mathlib;

        uses MathUnit;

        type
          TVec3 = record X, Y, Z: Integer end;

        var
          Loaded: Integer;

        function Add(A, B: Integer): Integer; stdcall;
        begin
          Result := A + B;
        end;

        function MakeVec(X, Y, Z: Integer): TVec3; cdecl;     // 12 bytes: hidden result pointer
        begin
          Result.X := X; Result.Y := Y; Result.Z := Z;
        end;

        procedure Reset;
        begin
          Loaded := 0;
        end;

        exports
          Add,                          // name 'Add', ordinal 1
          MakeVec name 'make_vec' index 7,
          MathUnit.Twice,
          Reset name 'reset_1' index 2,
          Reset name 'Reset';

        begin
          Loaded := Twice(21);
        end.
        """;

    internal const string MathUnitSource = """
        unit MathUnit;
        interface
        function Twice(A: Integer): Integer;
        implementation
        function Twice(A: Integer): Integer;
        begin
          Result := A * 2;
        end;
        end.
        """;

    /// <summary>The <c>.ll</c> of the library file <paramref name="name"/> (stage <c>ll</c>, in memory, the RTL's <c>System</c>).</summary>
    internal static string EmitLibraryIr(string name, string source, params (string Name, string Source)[] units) => Emit(name, source, units)[name];

    internal static Dictionary<string, string> Emit(string name, string source, params (string Name, string Source)[] units)
    {
        var fs = new InMemoryFileSystem().Add(Path.Combine(Dir, name + ".pas"), source).AddSystem();
        foreach (var (unit, text) in units)
        {
            fs.Add(Path.Combine(Dir, unit + ".pas"), text);
        }

        var options = new CompilationOptions { MainFile = Path.Combine(Dir, name + ".pas"), RtlDirectory = @"C:\rtl", ObjectDirectory = @"C:\p\obj" };
        var result = Compilation.Create(options, [], fs).Emit(new EmitOptions { Stage = EmitStage.Ll }, TestContext.Current.CancellationToken);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.ToText())));
        return result.CompiledUnits.ToDictionary(u => u, u => fs.ReadAllText(Path.Combine(@"C:\p\obj", u + ".ll")));
    }

    private static string MathLib() => EmitLibraryIr("mathlib", MathLibSource, ("MathUnit", MathUnitSource));

    [Fact]
    [Trait("Req", "CMP-1054")]
    [Trait("Req", "LANG-550")]
    public void HiddenResultThunkReturnsPointer()
    {
        var text = MathLib();

        // the routine itself returns void through the hidden pointer (ABI-081) ...
        Assert.Contains("define internal void @\"mathlib.MakeVec\"(ptr %Result, i32 %X, i32 %Y, i32 %Z) #0 {\n", text);
        // ... the thunk passes the pointer through and returns it, as the platform convention requires of a callee
        Assert.Contains(
            "define ptr @make_vec(ptr %Result, i32 %X, i32 %Y, i32 %Z) #0 {\n" +
            "entry:\n" +
            "  call void @\"mathlib.MakeVec\"(ptr %Result, i32 %X, i32 %Y, i32 %Z)\n" +
            "  ret ptr %Result\n" +
            "}\n", text);
    }

    [Fact]
    [Trait("Req", "CMP-1053")]
    public void Thunks_OnePerExport_InOrdinalOrder_AfterAllOtherFunctions()
    {
        var text = MathLib();

        Assert.Contains(
            "define i32 @Add(i32 %A, i32 %B) #0 {\n" +
            "entry:\n" +
            "  %r = call i32 @\"mathlib.Add\"(i32 %A, i32 %B)\n" +
            "  ret i32 %r\n" +
            "}\n", text);
        // a routine of a used unit is only declared here; a procedure's thunk returns void
        Assert.Contains("declare i32 @\"MathUnit.Twice\"(i32)\n", text);
        Assert.Contains(
            "define i32 @Twice(i32 %A) #0 {\n" +
            "entry:\n" +
            "  %r = call i32 @\"MathUnit.Twice\"(i32 %A)\n" +
            "  ret i32 %r\n" +
            "}\n", text);
        Assert.Contains(
            "define void @reset_1() #0 {\n" +
            "entry:\n" +
            "  call void @\"mathlib.Reset\"()\n" +
            "  ret void\n" +
            "}\n", text);

        // ordinals 1, 2, 3, 4, 7: Add, reset_1, Twice, Reset, make_vec - the same routine under two names has two thunks
        var defined = text.Split('\n').Where(l => l.StartsWith("define ", StringComparison.Ordinal)).Select(l => l[(l.IndexOf('@') + 1)..l.IndexOf('(')]).ToList();
        Assert.Equal(["Add", "reset_1", "Twice", "Reset", "make_vec"], defined[^5..]);
        Assert.Equal("_dllstart", defined[^6]);
    }

    [Fact]
    [Trait("Req", "CMP-1050")]
    [Trait("Req", "CMP-1051")]
    [Trait("Req", "CMP-1052")]
    public void LibraryModule_HasUnitTableMainAndEntryRoutine_NoStart()
    {
        var text = MathLib();

        // $units: System, MathUnit (no sections: nulls) - the library has no managed globals, so no entry of its own
        Assert.Contains("@\"$units\" = internal constant [2 x { ptr, ptr }] ", text);
        Assert.Contains("define internal void @\"$main\"() #0 {\nentry:\n  call void @\"System._InitUnits\"()\n", text);
        Assert.DoesNotContain("@_start", text);
        Assert.DoesNotContain("System._Startup", text);
        Assert.DoesNotContain("System._Halt0", text);
        Assert.Contains(
            "define i32 @_dllstart(ptr %hinst, i32 %reason, ptr %reserved) #0 personality ptr @__C_specific_handler {\n" +
            "entry:\n" +
            "  %init = call i32 @\"System._DllEntry\"(ptr %hinst, i32 %reason, ptr %reserved, ptr @\"$units\", i64 2)\n" +
            "  %run = icmp ne i32 %init, 0\n" +
            "  br i1 %run, label %attach, label %ok\n" +
            "attach:\n" +
            "  invoke void @\"$main\"()\n" +
            "          to label %attached unwind label %catch.dispatch\n" +
            "attached:\n" +
            "  call void @\"System._DllInitDone\"()\n" +
            "  br label %ok\n" +
            "ok:\n" +
            "  ret i32 1\n" +
            "catch.dispatch:\n" +
            "  %cs = catchswitch within none [label %catch] unwind to caller\n" +
            "catch:\n" +
            "  %cp = catchpad within %cs [ptr @\"System._SehFilter\"]\n" +
            "  catchret from %cp to label %failed\n" +
            "failed:\n" +
            "  %t1 = call ptr @\"System._BeginCatch\"()\n" +
            "  call void @\"System._DllInitFailed\"()\n" +
            "  ret i32 0\n" +
            "}\n", text);
        Assert.Contains("declare i32 @\"System._DllEntry\"(ptr, i32, ptr, ptr, i64)\n", text);
        Assert.Contains("declare void @\"System._DllInitDone\"()\n", text);
        Assert.Contains("declare void @\"System._DllInitFailed\"()\n", text);
    }

    [Fact]
    [Trait("Req", "CMP-1050")]
    public void ManagedGlobals_AreFinalisedThroughTheUnitTable_NotAtTheEndOfMain()
    {
        var text = EmitLibraryIr("L", "library L;\nvar S: string;\nprocedure P; begin end;\nexports P;\nbegin\n  S := 'x';\nend.");

        // the library's own (last) entry: no $init, its $final releases S when the library is unloaded
        Assert.Contains("@\"$units\" = internal constant [2 x { ptr, ptr }] [{ ptr, ptr } { ptr null, ptr null }, { ptr, ptr } { ptr null, ptr @\"L.$final\" }], align 8\n", text);
        Assert.Contains("define internal void @\"L.$final\"() #0", text);
        Assert.Contains("@\"System._DllEntry\"(ptr %hinst, i32 %reason, ptr %reserved, ptr @\"$units\", i64 2)", text);
        var main = text[text.IndexOf("define internal void @\"$main\"()", StringComparison.Ordinal)..];
        main = main[..main.IndexOf("\n}\n", StringComparison.Ordinal)];
        Assert.DoesNotContain("_UStrClr", main);
        Assert.DoesNotContain("L.$final", main);
    }

    [Fact]
    [Trait("Req", "CMP-1050")]
    public void LibraryWithoutBlockOrExports_StillHasMainAndEntryRoutine()
    {
        var text = EmitLibraryIr("Empty", "library Empty;\nend.");

        Assert.Contains("define internal void @\"$main\"() #0 {\nentry:\n  call void @\"System._InitUnits\"()\n  br label %exit\nexit:\n  ret void\n}\n", text);
        Assert.Contains("define i32 @_dllstart(ptr %hinst, i32 %reason, ptr %reserved) #0 personality ptr @__C_specific_handler {\n", text);
        Assert.EndsWith("_dllstart", text.Split('\n').Last(l => l.StartsWith("define ", StringComparison.Ordinal)).Split('(')[0]);
    }

    [Fact]
    [Trait("Req", "CMP-1052")]
    public void Program_DoesNotReferenceTheLibraryHelpers()
    {
        var text = EmitLibraryIr("P", "program P;\nbegin\nend.");

        Assert.Contains("define void @_start()", text);
        Assert.DoesNotContain("_dllstart", text);
        Assert.DoesNotContain("_DllEntry", text);
        Assert.DoesNotContain("_DllInit", text);
    }

    [Fact]
    [Trait("Req", "CMP-1054")]
    public void PublicNameExportHasNoThunk()
    {
        const string unit = "unit U;\ninterface\nfunction Three: Integer; public name 'three';\nimplementation\nfunction Three: Integer; begin Result := 3; end;\nend.";
        var modules = Emit("L", "library L;\nuses U;\nfunction Version: Integer; public name 'lib_version';\nbegin\n  Result := 7;\nend;\n"
            + "exports Version name 'lib_version', Version name 'version2', U.Three name 'three';\nend.", ("U", unit));
        var text = modules["L"];

        // exported under exactly its public name: the routine's own symbol (external linkage) is the export
        Assert.Contains("define i32 @lib_version() #0 {\n", text);
        Assert.Single(text.Split('\n'), l => l.StartsWith("define ", StringComparison.Ordinal) && l.Contains("@lib_version(", StringComparison.Ordinal));
        // under another name the same routine gets a thunk
        Assert.Contains("define i32 @version2() #0 {\nentry:\n  %r = call i32 @lib_version()\n  ret i32 %r\n}\n", text);
        // a used unit's routine with that public name: defined by the unit's module, only declared here
        Assert.Contains("declare i32 @three()\n", text);
        Assert.DoesNotContain("define i32 @three(", text);
        Assert.Contains("define i32 @three() #0 {\n", modules["U"]);
    }

    [Fact]
    [Trait("Req", "CMP-1053")]
    [Trait("Req", "CMP-1011")]
    public void Thunk_MirrorsOpenArrayHighCompanionsAndByAddressParameters()
    {
        var text = EmitLibraryIr("L", """
            library L;
            type TBig = record A, B, C: Int64; end;
            {$WARN EXPORT_UNSAFE_TYPE OFF}
            function Sum(const Values: array of Integer; var Total: Int64; Big: TBig; D: Double): Boolean;
            begin
              Total := Length(Values) + Big.A;
              Result := D > 0;
            end;
            exports Sum;
            end.
            """);

        Assert.Contains(
            "define i8 @Sum(ptr %Values, i64 %Values$high, ptr %Total, ptr %Big, double %D) #0 {\n" +
            "entry:\n" +
            "  %r = call i8 @\"L.Sum\"(ptr %Values, i64 %Values$high, ptr %Total, ptr %Big, double %D)\n" +
            "  ret i8 %r\n" +
            "}\n", text);
    }

    public static TheoryData<string> Optimisations() => ["-O0", "-O2"];

    [Theory]
    [MemberData(nameof(Optimisations))]
    [Trait("Kind", "Native")]
    [Trait("Category", "Toolchain")]
    [Trait("Req", "CMP-1051")]
    [Trait("Req", "CMP-1053")]
    public async Task LibraryModule_AssemblesWithPinnedClang(string optimisation)
    {
        var (exitCode, output) = await Clang.CompileToObjectAsync(MathLib(), optimisation);

        Assert.True(exitCode == 0, "clang failed:\n" + output);
    }
}
