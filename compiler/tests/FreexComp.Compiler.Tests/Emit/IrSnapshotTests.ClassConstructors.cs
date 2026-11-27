using FreexComp.Compiler.Text;
using FreexComp.Tests.Shared;

namespace FreexComp.Compiler.Tests.Emit;

/// <summary>C-374 (generics.md §14.4, §14.5): class constructors and class destructors of non-generic types (CMP-1395…1397, CMP-1400).</summary>
public sealed partial class IrSnapshotTests
{
    /// <summary>A class with both, a record with a class constructor only, a nested class with a class destructor only; both sections and a managed global.</summary>
    internal const string ClassConstructorsSource = """
        unit M;
        interface
        type
          TA = class
            class var Count: Integer;
            class constructor Create;
            class destructor Destroy;
          public
            type
              TN = class
                class destructor Done;
              end;
          end;
          TR = record
            X: Integer;
            class constructor Init;
          end;
        var
          Name: string;
        implementation
        class constructor TA.Create;
        begin
          Count := 1;
        end;
        class destructor TA.Destroy;
        begin
          Count := 0;
        end;
        class destructor TA.TN.Done;
        begin
          TA.Count := 2;
        end;
        class constructor TR.Init;
        begin
          TA.Count := 3;
        end;
        initialization
          Name := 'init';
        finalization
          Name := '';
        end.
        """;

    internal const string ClassConstructorDebugSource = """
        unit M;
        interface
        type
          TA = class
            class var Count: Integer;
            class constructor Create;
          end;
        implementation
        class constructor TA.Create;
        begin
          Count := 42;
        end;
        initialization
          TA.Count := TA.Count + 1;
        end.
        """;

    internal const string ProgramClassConstructorsSource = """
        program P;
        type
          TP = record
            X: Integer;
            class constructor Create;
            class destructor Destroy;
          end;
        var
          Runs: Integer;
        class constructor TP.Create;
        begin
          Runs := 1;
        end;
        class destructor TP.Destroy;
        begin
          Runs := 0;
        end;
        begin
          Runs := Runs + 1;
        end.

        """;

    /// <summary>C-375: an own class with a class constructor and the instance <c>TBox&lt;Integer&gt;</c> of a generic with both.</summary>
    internal const string ClassConstructorsOfInstancesSource = """
        unit M;
        interface
        type
          TBox<T> = class
            class var Size: Integer;
            class constructor Create;
            class destructor Destroy;
          end;
          TA = class
            class constructor Create;
          end;
        var
          Box: TBox<Integer>;
        implementation
        class constructor TBox<T>.Create;
        begin
          Size := SizeOf(T);
        end;
        class destructor TBox<T>.Destroy;
        begin
          Size := 0;
        end;
        class constructor TA.Create;
        begin
          TBox<Integer>.Size := 1;
        end;
        end.
        """;

    /// <summary>
    /// CMP-1395, CMP-1397 (C-375): the <c>$cctor</c> / <c>$cdtor</c> of the instance are <c>linkonce_odr</c> in their own comdats, its
    /// image-wide claim <c>$ccstate</c> is a <c>linkonce_odr</c> byte with its comdat; the constructor part stores the claim before
    /// the call and the done flag after it, the destructor part tests the done flag only.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1395")]
    [Trait("Req", "CMP-1397")]
    [Trait("Req", "LANG-790")]
    public void ClassConstructorsOfInstances()
    {
        var text = Emit(ClassConstructorsOfInstancesSource);
        Snapshot.Verify("ClassConstructorsOfInstances", text);
        Assert.Contains("$\"M.TBox<Integer>.$ccstate\" = comdat any\n", text);
        Assert.Contains("@\"M.TBox<Integer>.$ccstate\" = linkonce_odr global i8 0, comdat, align 1\n", text);
        Assert.Contains("@\"M.$ccdone\" = internal global [2 x i8] zeroinitializer, align 1\n", text);
        Assert.Contains("define linkonce_odr void @\"M.TBox<Integer>.$cctor\"() #0 comdat {", text);
        Assert.Contains("define linkonce_odr void @\"M.TBox<Integer>.$cdtor\"() #0 comdat {", text);
        Assert.Contains("define internal void @\"M.TA.$cctor\"() #0 {", text);
        Assert.Contains(
            "  call void @\"M.TA.$cctor\"()\n" +
            "  store i8 1, ptr @\"M.$ccdone\", align 1\n" +
            "  %cc1.state = load i8, ptr @\"M.TBox<Integer>.$ccstate\", align 1\n" +
            "  %cc1.free = icmp eq i8 %cc1.state, 0\n" +
            "  br i1 %cc1.free, label %cc1.run, label %cc1.done\n" +
            "cc1.run:\n" +
            "  store i8 1, ptr @\"M.TBox<Integer>.$ccstate\", align 1\n" +
            "  call void @\"M.TBox<Integer>.$cctor\"()\n" +
            "  store i8 1, ptr getelementptr inbounds ([2 x i8], ptr @\"M.$ccdone\", i64 0, i64 1), align 1\n" +
            "  br label %cc1.done\n" +
            "cc1.done:\n", text);
        Assert.Contains(
            "  %cd1.done = load i8, ptr getelementptr inbounds ([2 x i8], ptr @\"M.$ccdone\", i64 0, i64 1), align 1\n" +
            "  %cd1.ran = icmp ne i8 %cd1.done, 0\n" +
            "  br i1 %cd1.ran, label %cd1.run, label %cd1.skip\n" +
            "cd1.run:\n" +
            "  call void @\"M.TBox<Integer>.$cdtor\"()\n" +
            "  br label %cd1.skip\n" +
            "cd1.skip:\n", text);
    }

    /// <summary>
    /// CMP-1395…1397: <c>$ccdone</c>, internal <c>$cctor</c> / <c>$cdtor</c> with fixed suffixes, the constructor parts in <c>$init</c>
    /// before the initialization statements, the destructor parts in reverse order in <c>$final</c> after the finalization statements
    /// and before the release of the managed global.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1395")]
    [Trait("Req", "CMP-1396")]
    [Trait("Req", "CMP-1397")]
    public void ClassConstructors()
    {
        var text = Emit(ClassConstructorsSource);
        Snapshot.Verify("ClassConstructors", text);
        Assert.Contains("@\"M.$ccdone\" = internal global [3 x i8] zeroinitializer, align 1\n", text);
        Assert.Contains("define internal void @\"M.TA.$cctor\"() #0 {", text);
        Assert.Contains("define internal void @\"M.TA.$cdtor\"() #0 {", text);
        Assert.Contains("define internal void @\"M.TA.TN.$cdtor\"() #0 {", text);
        Assert.Contains("define internal void @\"M.TR.$cctor\"() #0 {", text);
        Assert.Contains(
            "  call void @\"M.TA.$cctor\"()\n" +
            "  store i8 1, ptr @\"M.$ccdone\", align 1\n" +
            "  store i8 1, ptr getelementptr inbounds ([3 x i8], ptr @\"M.$ccdone\", i64 0, i64 1), align 1\n" +
            "  call void @\"M.TR.$cctor\"()\n" +
            "  store i8 1, ptr getelementptr inbounds ([3 x i8], ptr @\"M.$ccdone\", i64 0, i64 2), align 1\n", text);
        Assert.Contains(
            "  %cd1.done = load i8, ptr getelementptr inbounds ([3 x i8], ptr @\"M.$ccdone\", i64 0, i64 1), align 1\n" +
            "  %cd1.ran = icmp ne i8 %cd1.done, 0\n" +
            "  br i1 %cd1.ran, label %cd1.run, label %cd1.skip\n" +
            "cd1.run:\n" +
            "  invoke void @\"M.TA.TN.$cdtor\"()\n" +                 // inside the cleanup scope of the managed global
            "          to label %cont.3 unwind label %cleanup.pad\n" +
            "cont.3:\n" +
            "  br label %cd1.skip\n" +
            "cd1.skip:\n" +
            "  %cd0.done = load i8, ptr @\"M.$ccdone\", align 1\n", text);
        var init = text[text.IndexOf("define void @\"M.$init\"", StringComparison.Ordinal)..];
        Assert.True(init.IndexOf("@\"M.TR.$cctor\"", StringComparison.Ordinal) < init.IndexOf("@\"M.Name\"", StringComparison.Ordinal));
        var final = text[text.IndexOf("define void @\"M.$final\"", StringComparison.Ordinal)..];
        var statement = final.IndexOf("@\"M.Name\"", StringComparison.Ordinal);
        var destructor = final.IndexOf("void @\"M.TA.$cdtor\"()", StringComparison.Ordinal);
        var release = final.IndexOf("  call void @\"System._UStrClr\"(ptr @\"M.Name\")\n  br label %exit", StringComparison.Ordinal);
        Assert.True(statement < destructor && destructor < release, "finalization statements, class destructors, then the managed global");
    }

    /// <summary>
    /// CMP-1397, CMP-1396: a program's class constructors are called in <c>$main</c> after <c>_InitUnits</c>, before the main block;
    /// its class destructor runs from the program's own <c>$final</c> entry, which exists without managed globals.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1396")]
    [Trait("Req", "CMP-1397")]
    [Trait("Req", "LANG-789")]
    public void ProgramClassConstructors()
    {
        var fs = new InMemoryFileSystem().Add(@"C:\p\P.pas", ProgramClassConstructorsSource).AddSystem();
        var options = new CompilationOptions { MainFile = @"C:\p\P.pas", RtlDirectory = @"C:\rtl", ObjectDirectory = @"C:\p\obj" };
        var result = Compilation.Create(options, [], fs).Emit(new EmitOptions { Stage = EmitStage.Ll }, TestContext.Current.CancellationToken);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.ToText())));
        var text = fs.ReadAllText(@"C:\p\obj\P.ll");

        Snapshot.Verify("ProgramClassConstructors", text);
        Assert.Contains(
            "  call void @\"System._InitUnits\"()\n" +
            "  call void @\"P.TP.$cctor\"()\n" +
            "  store i8 1, ptr @\"P.$ccdone\", align 1\n", text);
        Assert.Contains("{ ptr null, ptr @\"P.$final\" }]", text);
        Assert.Contains("define internal void @\"P.$final\"() #0 {", text);
    }

    /// <summary>
    /// CMP-1400: the <c>DISubprogram</c> of a class constructor is named <c>TA.Create</c> with its link name as linkage name; the call
    /// inserted into <c>$init</c> carries the location of the declaration heading (line 6).
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1400")]
    public void DebugInfo_ClassConstructor()
    {
        var text = DebugInfoTests.EmitWithDebugInfo(ClassConstructorDebugSource);
        Snapshot.Verify("DebugInfo_ClassConstructor", text);
        Assert.Matches("distinct !DISubprogram\\(name: \"TA\\.Create\", linkageName: \"M\\.TA\\.\\$cctor\", .*line: 9, .*spFlags: DISPFlagDefinition \\| DISPFlagLocalToUnit,", text);
        var call = System.Text.RegularExpressions.Regex.Match(text, "  call void @\"M\\.TA\\.\\$cctor\"\\(\\), !dbg !(\\d+)\n");
        Assert.True(call.Success, "the inserted call has a location");
        Assert.Matches("!" + call.Groups[1].Value + " = !DILocation\\(line: 6, column: 5, scope: ", text);
    }
}
