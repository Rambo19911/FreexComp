using System.Text;
using System.Text.Json.Nodes;
using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Emit;
using FreexComp.Compiler.Lowering;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;
using FreexComp.Compiler.Types;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Emit;

public sealed partial class PdiSidecarTests
{
    private const string EntryTypes = """
        type
          PEntry = ^TEntry;
          TEntry = record
            V: Integer;
            Next: PEntry;
          end;
        """;

    /// <summary>
    /// C-366 (DBG-146, debug-info-sidecar.md §4): a record that points back at itself through a named pointer type gets only type
    /// references the debugger can resolve — declared in the program, and declared in a unit the program reads from its
    /// <c>.pcu</c> (the second build). The named pointer carries its unit and is described once.
    /// </summary>
    [Theory]
    [InlineData("program")]
    [InlineData("unit")]
    [Trait("Category", "Toolchain")]
    [Trait("Req", "DBG-146")]
    public void SelfReferentialRecordThroughNamedPointer_HasNoUnknownTypeIds(string declaredIn)
    {
        var dir = PathUtil.Normalize(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var body = "begin\n  New(P);\n  P^.V := 1;\n  P^.Next := nil;\n  WriteLn(P^.V);\n  Dispose(P);\nend.\n";
            string unit;
            if (declaredIn == "program")
            {
                unit = "Q";
                Put(dir, "Q", "program Q;\n" + EntryTypes + "\nvar\n  P: PEntry;\n\n" + body);
            }
            else
            {
                unit = "U";
                Put(dir, "U", "unit U;\ninterface\n" + EntryTypes + "\nfunction Count(P: PEntry): Integer;\nimplementation\n"
                    + "function Count(P: PEntry): Integer;\nbegin\n  Result := 0;\n  while P <> nil do\n  begin\n    Inc(Result);\n    P := P^.Next;\n  end;\nend;\nend.\n");
                Put(dir, "Q", "program Q;\nuses U;\nvar\n  P: PEntry;\n\n" + body);
            }

            var first = BuildWithDebugInfo(dir, "Q");
            AssertEntryTypes(LoadPdi(dir, "Q"), unit);
            if (declaredIn == "unit")
            {
                Assert.Contains("U", first.CompiledUnits);
                Put(dir, "Q", "program Q;\nuses U;\nvar\n  P: PEntry;\n\n" + body.Replace("P^.V := 1;", "P^.V := Count(P);", StringComparison.Ordinal));
                var second = BuildWithDebugInfo(dir, "Q");
                Assert.Equal(["Q"], second.CompiledUnits);
                Assert.Contains("U", second.UpToDateUnits);                   // the program read U through its .pcu
                AssertEntryTypes(LoadPdi(dir, "Q"), unit);
            }
        }
        finally
        {
            TestDirectories.Delete(dir);
        }
    }

    /// <summary>
    /// C-366: the sidecar of a program using <c>Classes</c> (whose units declare self-referential records behind named pointers)
    /// passes the checks the debugger's loader makes: unique positive ids and every type reference resolves.
    /// </summary>
    [Fact]
    [Trait("Category", "Toolchain")]
    [Trait("Req", "DBG-146")]
    public void ProgramUsingClasses_SidecarLoads()
    {
        var dir = PathUtil.Normalize(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            Put(dir, "LibStep", "program LibStep;\nuses Classes;\nvar\n  L: TStringList;\nbegin\n  L := TStringList.Create;\n  L.Add('a');\n  WriteLn(L.Count);\n  L.Free;\nend.\n");
            BuildWithDebugInfo(dir, "LibStep");

            var pdi = LoadPdi(dir, "LibStep");

            AssertLoadable(pdi);
            SidecarTests.ValidateSchema(pdi);
            Assert.Contains(pdi["units"]!.AsArray(), u => u!["name"]!.GetValue<string>() == "Classes");
        }
        finally
        {
            TestDirectories.Delete(dir);
        }
    }

    /// <summary>
    /// C-366 with CMP-1393: a named pointer nested in a class carries its unit and the display name <c>TOuter.PNode</c>, so two
    /// nested pointers of one name stay two entries (unified by unit + name in the merge).
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-1393")]
    [Trait("Req", "DBG-146")]
    public void NestedNamedPointer_CarriesUnitAndDisplayName()
    {
        const string Source = """
            program P;
            type
              TA = class
              public
                type
                  PNode = ^TNode;
                  TNode = record
                    Next: PNode;
                  end;
              end;
              TB = class
              public
                type
                  PNode = ^TItem;
                  TItem = record
                    Value: Integer;
                    Next: PNode;
                  end;
              end;
            var
              A: TA.PNode;
              B: TB.PNode;
            begin
              A := nil;
              B := nil;
            end.
            """;
        var tree = SyntaxTree.Parse(SourceText.From(Path, Source));
        var builtIns = new BuiltInTypes();
        var system = Binder.BindCompilationUnit(SyntaxTree.Parse(SourceText.From(@"C:\p\System.pas", ClosureSystemSource)), builtIns);
        var result = Binder.BindCompilationUnit(tree, builtIns, system);
        Assert.Empty(Format(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        var options = new DebugInfoOptions(DebugFormat.CodeView, IsOptimized: false) { ReadFile = p => PathUtil.Comparer.Equals(p, Path) ? Encoding.UTF8.GetBytes(Source) : null };
        var fragment = IrEmitter.EmitUnit(Lowerer.Lower(result), new LlvmTextWriter(), debug: options)!;

        var merged = JsonNode.Parse(PdiSidecar.Merge(FreexComp.Compiler.Targets.TargetInfo.Win64, "P", "codeview", [fragment, fragment]))!.AsObject();

        AssertLoadable(merged);
        var types = merged["types"]!.AsArray().Select(t => t!.AsObject()).ToDictionary(t => t["id"]!.GetValue<int>());
        string Describe(string name)
        {
            var pointer = Assert.Single(types.Values, t => t["name"]!.GetValue<string>() == name);
            var target = types[pointer["to"]!.GetValue<int>()];
            var next = target["fields"]!.AsArray().Single(f => f!["name"]!.GetValue<string>() == "Next")!["type"]!.GetValue<int>();
            return $"{pointer["unit"]}:{target["name"]}:{next == pointer["id"]!.GetValue<int>()}";
        }

        Assert.Equal("P:TA.TNode:True", Describe("TA.PNode"));
        Assert.Equal("P:TB.TItem:True", Describe("TB.PNode"));
    }

    /// <summary>C-366: types without a unit that reach each other (a cycle no named type breaks) still get ids, never a placeholder.</summary>
    [Fact]
    [Trait("Req", "DBG-146")]
    public void Merge_CycleThroughTypesWithoutUnit_ResolvesEveryReference()
    {
        const string Fragment = """
            {"formatVersion":1,"program":"A","container":"codeview","abi":{},
             "types":[
               {"id":1,"name":"","kind":"pointer","size":8,"to":2},
               {"id":2,"name":"","kind":"record","size":8,"fields":[{"name":"Next","type":1,"offset":0}]}],
             "units":[{"name":"A","file":"A.pas","globals":[{"name":"G","type":1,"linkageName":"A.G"}],"routines":[]}]}
            """;

        var merged = JsonNode.Parse(PdiSidecar.Merge(FreexComp.Compiler.Targets.TargetInfo.Win64, "A", "codeview", [Fragment, Fragment]))!.AsObject();

        AssertLoadable(merged);
        var types = merged["types"]!.AsArray().Select(t => t!.AsObject()).ToDictionary(t => t["id"]!.GetValue<int>());
        var pointer = types[merged["units"]![0]!["globals"]![0]!["type"]!.GetValue<int>()];
        Assert.Equal("pointer", pointer["kind"]!.GetValue<string>());
        var record = types[pointer["to"]!.GetValue<int>()];
        Assert.Equal(pointer["id"]!.GetValue<int>(), record["fields"]![0]!["type"]!.GetValue<int>());
    }

    /// <summary>C-366: the writer refuses a type reference that names no entry (a negative placeholder included).</summary>
    [Fact]
    [Trait("Req", "DBG-146")]
    public void CheckTypeReferences_RejectsAnUnknownId()
    {
        var sidecar = JsonNode.Parse("""
            {"types":[{"id":1,"name":"","kind":"pointer","size":8,"to":-1}],"units":[]}
            """)!.AsObject();

        var error = Assert.Throws<InvalidOperationException>(() => PdiSidecar.CheckTypeReferences(sidecar));

        Assert.Contains("-1", error.Message, StringComparison.Ordinal);
    }

    private static void Put(string dir, string name, string text) =>
        PhysicalFileSystem.Instance.WriteAllBytes(System.IO.Path.Combine(dir, name + ".pas"), Encoding.UTF8.GetBytes(text));

    private static EmitResult BuildWithDebugInfo(string dir, string program)
    {
        Assert.True(File.Exists(Clang.Path), $"Pinned toolchain missing: {Clang.Path} (run scripts/bootstrap.ps1).");
        var options = new CompilationOptions
        {
            MainFile = System.IO.Path.Combine(dir, program + ".pas"),
            RtlDirectory = TestRtl.Directory,
            ToolchainDirectory = System.IO.Path.GetDirectoryName(Clang.Path)!,
            ObjectDirectory = System.IO.Path.Combine(dir, "obj"),
            DebugInfo = true,
        };
        var result = Compilation.Create(options, [], PhysicalFileSystem.Instance)
            .Emit(new EmitOptions { OutputPath = System.IO.Path.Combine(dir, program + ".exe") }, TestContext.Current.CancellationToken);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.ToText())));
        return result;
    }

    private static JsonObject LoadPdi(string dir, string program) =>
        JsonNode.Parse(File.ReadAllBytes(System.IO.Path.Combine(dir, program + ".pdi")))!.AsObject();

    private static void AssertEntryTypes(JsonObject pdi, string unit)
    {
        AssertLoadable(pdi);
        var types = pdi["types"]!.AsArray().Select(t => t!.AsObject()).ToList();
        var pointer = Assert.Single(types, t => t["name"]!.GetValue<string>() == "PEntry");
        var record = Assert.Single(types, t => t["name"]!.GetValue<string>() == "TEntry");
        Assert.Equal(unit, pointer["unit"]?.GetValue<string>());
        Assert.Equal(record["id"]!.GetValue<int>(), pointer["to"]!.GetValue<int>());
        Assert.Equal(["V", "Next"], record["fields"]!.AsArray().Select(f => f!["name"]!.GetValue<string>()));
        Assert.Equal(pointer["id"]!.GetValue<int>(), record["fields"]![1]!["type"]!.GetValue<int>());
    }

    /// <summary>
    /// The checks of the debugger's sidecar loader (debug-info-sidecar.md §4): every type id is a unique positive integer and every
    /// type reference — of a type, a field, a global, a routine's <c>self</c> and its variables — names one of them.
    /// </summary>
    private static void AssertLoadable(JsonObject pdi)
    {
        var ids = new HashSet<int>();
        foreach (var type in pdi["types"]!.AsArray())
        {
            var id = type!["id"]!.GetValue<int>();
            Assert.True(id > 0 && ids.Add(id), $"type id {id} is not a unique positive integer");
        }

        var unknown = new List<string>();
        void Check(JsonNode? owner, string member, string where)
        {
            if (owner?[member] is JsonValue value && value.TryGetValue<int>(out var id) && !ids.Contains(id))
            {
                unknown.Add($"{where} refers to unknown type id {id}");
            }
        }

        foreach (var type in pdi["types"]!.AsArray())
        {
            var where = "type " + type!["id"];
            foreach (var member in new[] { "base", "element", "indexType", "ancestor", "of", "to", "aliasOf" })
            {
                Check(type, member, where);
            }

            foreach (var field in type["fields"] as JsonArray ?? [])
            {
                Check(field, "type", where + " field " + field!["name"]);
            }
        }

        foreach (var unit in pdi["units"]!.AsArray())
        {
            var where = "unit " + unit!["name"];
            foreach (var global in unit["globals"] as JsonArray ?? [])
            {
                Check(global, "type", where + " global " + global!["name"]);
            }

            foreach (var routine in unit["routines"] as JsonArray ?? [])
            {
                Check(routine, "self", where + " routine " + routine!["linkageName"]);
                foreach (var variable in routine["vars"] as JsonArray ?? [])
                {
                    Check(variable, "type", where + " routine " + routine["linkageName"] + " var " + variable!["name"]);
                }
            }
        }

        Assert.True(unknown.Count == 0, string.Join("\n", unknown.Take(20)));
    }
}
