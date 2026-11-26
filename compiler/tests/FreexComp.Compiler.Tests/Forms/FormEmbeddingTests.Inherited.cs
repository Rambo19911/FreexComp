using FreexComp.Compiler.Tests.Emit;

namespace FreexComp.Compiler.Tests.Forms;

/// <summary>
/// C-343 (card F05-007, form-inheritance.md §5): the <c>{$FORM}</c> check for descendant files — the <c>inherited</c> objects of an
/// <c>inherited</c> root are checked with their contents (LANG-956), the root keyword must fit the class (E3105, E3106; LANG-957,
/// 958), and which classes have a form is known from the units of the compilation and from their <c>.pcu</c> (LANG-959, CMP-1381).
/// </summary>
public sealed partial class FormEmbeddingTests
{
    /// <summary>The component classes and the base form class; <c>TPanel</c> stands for a container with an event.</summary>
    private const string BaseTypes = """
          TNotify = procedure(Sender: TObject) of object;
          TMove = procedure(Sender: TObject; X: Integer) of object;
          TButton = class
          private
            FOnClick: TNotify;
          published
            property OnClick: TNotify read FOnClick write FOnClick;
          end;
          TLabel = class
          private
            FOnMove: TMove;
          published
            property OnMove: TMove read FOnMove write FOnMove;
          end;
          TPanel = class(TButton)
          end;
          TBase = class
          published
            Panel: TPanel;
            Ok: TButton;
            Lbl: TLabel;
            procedure BaseClick(Sender: TObject);
          end;

        """;

    private const string BaseMethods = "procedure TBase.BaseClick(Sender: TObject); begin end;\n";

    private const string ChildTypes = """
          TChild = class(PARENT)
          published
            FIELDS
            procedure Clicked(Sender: TObject);
            procedure Moved(Sender: TObject; X: Integer);
          end;

        procedure TChild.Clicked(Sender: TObject); begin end;
        procedure TChild.Moved(Sender: TObject; X: Integer); begin end;

        """;

    private const string BaseForm = """
        object Base: TBase
          object Panel: TPanel
            object Ok: TButton
              OnClick = BaseClick
            end
          end
          object Lbl: TLabel
          end
        end

        """;

    private const string Directives = "{$FORM Base.form}\n{$FORM Child.form}\n";

    /// <summary>A program declaring base and child class and embedding both forms (in <paramref name="directives"/> order).</summary>
    private static string InheritedProgram(string fields, string directives = Directives, string parent = "TBase") =>
        "program P;\ntype\n" + BaseTypes + ChildTypes.Replace("FIELDS", fields, StringComparison.Ordinal).Replace("PARENT", parent, StringComparison.Ordinal)
        + BaseMethods + "\n" + directives + "begin end.\n";

    private static string[] EmitInherited(string fields, string child, string? baseForm = BaseForm, string directives = Directives) =>
        Errors(Emit(InheritedProgram(fields, directives), baseForm is null
            ? [("Child.form", child.ReplaceLineEndings("\n"))]
            : [("Base.form", baseForm.ReplaceLineEndings("\n")), ("Child.form", child.ReplaceLineEndings("\n"))]).Result);

    private static int LineOf(string text, string line) => Array.IndexOf(text.Split('\n'), line) + 1;

    /// <summary>
    /// LANG-956: an override block of a descendant file names a component the root class inherits: a published field of an ancestor
    /// satisfies the field rule, nested blocks included; no field anywhere is E3101 at the block, a field of another type E3102.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-956")]
    public void InheritedRoot_OverrideBlocks_AncestorFieldSatisfies_MissingFieldE3101()
    {
        const string child = """
            inherited Child: TChild
              Caption = 'Child'
              inherited Panel: TPanel
                inherited Ok: TButton
                  Left = 4
                end
              end
              inherited Lbl: TLabel
              end
            end

            """;

        Assert.Empty(EmitInherited("Extra: TButton;", child));
        Assert.Equal(["Child.form(4,15): error E3101: Form 'Child': field 'Gone: TButton' is not declared in class 'TChild'"],
            EmitInherited("Extra: TButton;", child.Replace("inherited Ok:", "inherited Gone:", StringComparison.Ordinal)));
        Assert.Equal(["Child.form(4,15): error E3102: Form 'Child': declaration of 'Ok' is not compatible with the form (TLabel expected)"],
            EmitInherited("Extra: TButton;", child.Replace("inherited Ok: TButton", "inherited Ok: TLabel", StringComparison.Ordinal)));
        Assert.Equal(["Child.form(8,13): error E3101: Form 'Child': field 'Missing: TLabel' is not declared in class 'TChild'"],
            EmitInherited("Extra: TButton;", child.Replace("inherited Lbl:", "inherited Missing:", StringComparison.Ordinal)));
    }

    /// <summary>
    /// LANG-956: an <c>object</c> inside an override block (and one of the root) is an ordinary new component: it needs a published
    /// field of the root class or an ancestor and its handlers are checked; an <c>inherited</c> object inside such a new component
    /// keeps today's handling (field rule for the name, contents not checked; the reader reports F0107).
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-956")]
    public void InheritedRoot_NewChildrenNeedOwnField()
    {
        const string child = """
            inherited Child: TChild
              inherited Panel: TPanel
                object Help: TButton
                  OnClick = Clicked
                end
              end
              object NameEdit: TLabel
                inherited Inner: TButton
                  OnClick = Missing
                end
              end
            end

            """;
        const string fields = "Help: TButton; NameEdit: TLabel; Inner: TButton;";

        Assert.Empty(EmitInherited(fields, child));
        Assert.Equal(["Child.form(3,12): error E3101: Form 'Child': field 'Help: TButton' is not declared in class 'TChild'"],
            EmitInherited("NameEdit: TLabel; Inner: TButton;", child));
        Assert.Equal(["Child.form(3,12): error E3102: Form 'Child': declaration of 'Help' is not compatible with the form (TButton expected)"],
            EmitInherited("Help: TLabel; NameEdit: TLabel; Inner: TButton;", child));
        Assert.Equal(["Child.form(4,17): error E3101: Form 'Child': method 'Nope' is not declared in class 'TChild'"],
            EmitInherited(fields, child.Replace("= Clicked", "= Nope", StringComparison.Ordinal)));
        Assert.Equal(["Child.form(7,10): error E3101: Form 'Child': field 'NameEdit: TLabel' is not declared in class 'TChild'"],
            EmitInherited("Help: TButton; Inner: TButton;", child));
        Assert.Equal(["Child.form(8,15): error E3101: Form 'Child': field 'Inner: TButton' is not declared in class 'TChild'"],
            EmitInherited("Help: TButton; NameEdit: TLabel;", child));
    }

    /// <summary>
    /// LANG-956 (as LANG-953): the event lines of override blocks, nested ones included, are checked against the root class: a missing
    /// method is E3101, one that does not fit the visible event property E3102; a published method of the ancestor class is fine.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-956")]
    public void InheritedRoot_EventsCheckedAgainstTheRoot()
    {
        const string child = """
            inherited Child: TChild
              inherited Panel: TPanel
                OnClick = Clicked
                inherited Ok: TButton
                  OnClick = BaseClick
                end
              end
              inherited Lbl: TLabel
                OnMove = Moved
              end
            end

            """;
        const string fields = "Extra: TButton;";

        Assert.Empty(EmitInherited(fields, child));
        Assert.Equal(
            [
                "Child.form(3,15): error E3101: Form 'Child': method 'Missing' is not declared in class 'TChild'",
                "Child.form(5,17): error E3101: Form 'Child': method 'Missing' is not declared in class 'TChild'",
                "Child.form(9,14): error E3101: Form 'Child': method 'Missing' is not declared in class 'TChild'",
            ],
            EmitInherited(fields, child.Replace("= Clicked", "= Missing", StringComparison.Ordinal).Replace("= BaseClick", "= Missing", StringComparison.Ordinal)
                .Replace("= Moved", "= Missing", StringComparison.Ordinal)));
        Assert.Equal(["Child.form(5,17): error E3102: Form 'Child': declaration of 'Moved' is not compatible with the form (TNotify expected)"],
            EmitInherited(fields, child.Replace("= BaseClick", "= Moved", StringComparison.Ordinal)));
        Assert.Equal(["Child.form(9,14): error E3102: Form 'Child': declaration of 'Clicked' is not compatible with the form (TMove expected)"],
            EmitInherited(fields, child.Replace("OnMove = Moved", "OnMove = Clicked", StringComparison.Ordinal)));
    }

    /// <summary>
    /// LANG-957, 959: an <c>inherited</c> root whose class has no ancestor with a form is E3105 at the root's name — the ancestor
    /// declares no form (as a framework form class), or the ancestor's <c>{$FORM}</c> failed (E3100, F0001, E3103).
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-957")]
    [Trait("Req", "LANG-959")]
    public void InheritedRoot_NoAncestorForm_E3105()
    {
        const string child = "inherited Child: TChild\nend\n";
        const string e3105 = "Child.form(1,11): error E3105: Form 'Child': class 'TChild' has no ancestor with a form; the form file must start with 'object'";

        Assert.Empty(EmitInherited("Extra: TButton;", child));
        Assert.Equal([e3105], EmitInherited("Extra: TButton;", child, baseForm: null, directives: "{$FORM Child.form}\n"));

        var directiveLine = LineOf(InheritedProgram("Extra: TButton;"), "{$FORM Base.form}");
        Assert.Equal([$"P.pas({directiveLine},1): error E3100: Form file 'C:\\p\\Base.form' not found or unreadable".Replace(@"C:\p\", "", StringComparison.Ordinal), e3105],
            EmitInherited("Extra: TButton;", child, baseForm: null));

        var syntax = Emit(InheritedProgram("Extra: TButton;"), ("Base.form", "object Base: TBase\n  Left = = 1\nend\n"), ("Child.form", child)).Result;
        Assert.Equal(["F0001", "E3105"], syntax.Diagnostics.Select(d => d.Code));

        Assert.Equal(["Base.form(1,8): error E3103: Form 'Base': class 'TNope' is not declared in this unit", e3105],
            EmitInherited("Extra: TButton;", child, baseForm: "object Base: TNope\nend\n"));
    }

    /// <summary>
    /// LANG-958, 959: an <c>object</c> root whose class has an ancestor with a form is E3106 naming the nearest such ancestor — in
    /// another unit of the compilation (with a class without a form between), or in the same unit with its directive after the
    /// descendant's.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-958")]
    [Trait("Req", "LANG-959")]
    public void ObjectRoot_AncestorHasForm_E3106()
    {
        const string e3106 = "Child.form(1,8): error E3106: Form 'Child': class 'TChild' inherits the form of 'TBase'; the form file must start with 'inherited'";
        var unit = "unit B;\ninterface\ntype\n" + BaseTypes + "implementation\n{$FORM Base.form}\n" + BaseMethods + "end.\n";
        var program = "program P;\nuses B;\ntype\n  TMid = class(TBase)\n  end;\n" + ChildTypes.Replace("FIELDS", "Extra: TButton;", StringComparison.Ordinal)
            .Replace("PARENT", "TMid", StringComparison.Ordinal) + "{$FORM Child.form}\nbegin end.\n";

        Assert.Equal([e3106], Errors(Emit(program, ("B.pas", unit), ("Base.form", BaseForm), ("Child.form", "object Child: TChild\nend\n")).Result));
        Assert.Empty(Errors(Emit(program, ("B.pas", unit), ("Base.form", BaseForm), ("Child.form", "inherited Child: TChild\nend\n")).Result));

        Assert.Equal([e3106], EmitInherited("Extra: TButton;", "object Child: TChild\nend\n", directives: "{$FORM Child.form}\n{$FORM Base.form}\n"));
        Assert.Empty(EmitInherited("Extra: TButton;", "inherited Child: TChild\nend\n", directives: "{$FORM Child.form}\n{$FORM Base.form}\n"));
    }

    /// <summary>
    /// LANG-959, CMP-1381: the ancestor's unit loaded from its <c>.pcu</c> (no source): its forms are known from the key
    /// <c>forms</c>, so an <c>inherited</c> root has no E3105 and an <c>object</c> root gets E3106 exactly as with the source.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-959")]
    [Trait("Req", "CMP-1381")]
    public void AncestorFormKnownFromPcu()
    {
        const string e3106 = "Child.form(1,8): error E3106: Form 'Child': class 'TChild' inherits the form of 'TBase'; the form file must start with 'inherited'";
        const string lib = @"C:\lib";
        var unit = "unit B;\ninterface\ntype\n" + BaseTypes + "implementation\n{$FORM Base.form}\n" + BaseMethods + "end.\n";
        var program = "program P;\nuses B;\ntype\n" + ChildTypes.Replace("FIELDS", "Extra: TButton;", StringComparison.Ordinal)
            .Replace("PARENT", "TBase", StringComparison.Ordinal) + "{$FORM Child.form}\nbegin end.\n";
        const string inherited = "inherited Child: TChild\nend\n";
        const string @object = "object Child: TChild\nend\n";

        var sources = new InMemoryFileSystem().Add(@"C:\p\P.pas", program).Add(@"C:\p\B.pas", unit).Add(@"C:\p\Base.form", BaseForm)
            .Add(@"C:\p\Child.form", inherited).AddSystem();
        var options = new CompilationOptions { MainFile = @"C:\p\P.pas", RtlDirectory = @"C:\rtl", ObjectDirectory = @"C:\p\obj" };
        var pcu = Compilation.Create(options, [], sources).PcuTexts().Single(t => t.Unit == "B").Text;

        string[] Precompiled(string childForm)
        {
            var fs = new InMemoryFileSystem().Add(@"C:\p\P.pas", program).Add(@"C:\p\Child.form", childForm)
                .Add(Path.Combine(lib, "B.pcu"), pcu).Add(Path.Combine(lib, "B.obj"), "").AddSystem();
            var result = Compilation.Create(options with { SearchPaths = [lib] }, [], fs)
                .Emit(new EmitOptions { Stage = EmitStage.Ll }, TestContext.Current.CancellationToken);
            Assert.DoesNotContain(result.Diagnostics, d => d.Code == "E2202");      // B really came from its .pcu
            return Errors(result);
        }

        Assert.Equal(Errors(Emit(program, ("B.pas", unit), ("Base.form", BaseForm), ("Child.form", inherited)).Result), Precompiled(inherited));
        Assert.Empty(Precompiled(inherited));
        Assert.Equal([e3106], Errors(Emit(program, ("B.pas", unit), ("Base.form", BaseForm), ("Child.form", @object)).Result));
        Assert.Equal([e3106], Precompiled(@object));
    }
}
