using FreexComp.Compiler.Tests.Emit;

namespace FreexComp.Compiler.Tests.Forms;

/// <summary>C-114: <c>{$FORM}</c> through <see cref="Compilation.Emit"/> — E3100…E3104, F0001 pass-through, the CMP-415 table and its registration.</summary>
[Trait("Phase", "2")]
public sealed partial class FormEmbeddingTests
{
    private static (EmitResult Result, InMemoryFileSystem Fs) Emit(string main, params (string Name, string Text)[] files)
    {
        var fs = new InMemoryFileSystem().Add(@"C:\p\P.pas", main)
            .AddSystem();
        foreach (var (name, text) in files)
        {
            fs.Add(Path.Combine(@"C:\p", name), text);
        }

        var options = new CompilationOptions { MainFile = @"C:\p\P.pas", RtlDirectory = @"C:\rtl", ObjectDirectory = @"C:\p\obj" };
        var result = Compilation.Create(options, [], fs).Emit(new EmitOptions { Stage = EmitStage.Ll }, TestContext.Current.CancellationToken);
        return (result, fs);
    }

    private static string[] Errors(EmitResult result) => [.. result.Diagnostics.Select(d => d.ToText().Replace(@"C:\p\", "", StringComparison.Ordinal))];

    private const string FormTypes = """
        type
          TNotify = procedure(Sender: TObject) of object;
          TButton = class
          private
            FOnClick: TNotify;
          published
            property OnClick: TNotify read FOnClick write FOnClick;
          end;
          TForm1 = class
          published
            Button1: TButton;
            procedure Clicked(Sender: TObject);
            procedure Wrong(X: Integer);
          end;

        """;

    private const string FormMethods = """
        procedure TForm1.Clicked(Sender: TObject); begin end;
        procedure TForm1.Wrong(X: Integer); begin end;
        """;

    private const string FormClass = FormTypes + FormMethods;

    private const string Form = "object Form1: TForm1\n  object Button1: TButton\n    OnClick = Clicked\n  end\nend\n";

    [Fact]
    [Trait("Req", "LANG-670")]
    public void MissingFile_IsE3100AtTheDirective()
    {
        var (result, _) = Emit("program P;\n{$FORM Nope.form}\nbegin end.\n");

        Assert.Equal([@"P.pas(2,1): error E3100: Form file 'Nope.form' not found or unreadable"], Errors(result));
    }

    [Fact]
    [Trait("Req", "LANG-672")]
    public void SameClassTwice_IsE3104AtTheSecondDirective()
    {
        var (result, _) = Emit("program P;\n" + FormClass + "\n{$FORM A.form}\n{$FORM B.form}\nbegin end.\n", ("A.form", Form), ("B.form", Form));

        Assert.Equal(["P.pas(19,1): error E3104: Form class 'TForm1' is embedded more than once"], Errors(result));
    }

    [Fact]
    [Trait("Req", "LANG-670")]
    public void FormSyntaxError_IsAnF0001Error()
    {
        var (result, _) = Emit("program P;\n" + FormClass + "\n{$FORM A.form}\nbegin end.\n", ("A.form", "object Form1: TForm1\n  Left = = 1\nend\n"));

        var error = Assert.Single(result.Diagnostics);
        Assert.Equal(("F0001", DiagnosticSeverity.Error, @"C:\p\A.form", 1, 9), (error.Code, error.Severity, error.Location.Path,
            error.Location.LineSpan.Start.Line, error.Location.LineSpan.Start.Column));
        Assert.False(result.Success);
    }

    /// <summary>LANG-671: a handler whose signature does not fit the component's visible event property is E3102 at the handler name.</summary>
    [Fact]
    [Trait("Req", "LANG-671")]
    public void IncompatibleHandler_IsE3102()
    {
        var (result, _) = Emit("program P;\n" + FormClass + "\n{$FORM A.form}\nbegin end.\n", ("A.form", Form.Replace("Clicked", "Wrong", StringComparison.Ordinal)));

        Assert.Equal(["A.form(3,15): error E3102: Form 'Form1': declaration of 'Wrong' is not compatible with the form (TNotify expected)"], Errors(result));
    }

    // ------------------------------------------------------------------------------------------------ frames (C-342)

    /// <summary>A frame class with two published children and an event; the form class's fields replace <c>FIELDS</c>.</summary>
    private const string FrameProgram = """
        program P;
        type
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
          TAddressFrame = class
          private
            FOnResize: TNotify;
          published
            Street: TButton;
            Town: TLabel;
            property OnResize: TNotify read FOnResize write FOnResize;
            procedure FrameOnly(Sender: TObject);
          end;
          TForm1 = class
          published
            FIELDS
            procedure Clicked(Sender: TObject);
            procedure Moved(Sender: TObject; X: Integer);
          end;

        procedure TAddressFrame.FrameOnly(Sender: TObject); begin end;
        procedure TForm1.Clicked(Sender: TObject); begin end;
        procedure TForm1.Moved(Sender: TObject; X: Integer); begin end;

        {$FORM A.form}
        begin end.

        """;

    private static EmitResult EmitFrame(string fields, string form) =>
        Emit(FrameProgram.Replace("FIELDS", fields, StringComparison.Ordinal), ("A.form", form.ReplaceLineEndings("\n"))).Result;

    /// <summary>
    /// Card acceptance 4, LANG-952: an <c>inline</c> object is a component of the root like any <c>object</c>: it needs a published
    /// field of its frame class or an ancestor of it; missing → E3101 at the instance's name, another type → E3102.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-952")]
    public void Inline_MissingFrameField_E3101()
    {
        const string form = """
            object Form1: TForm1
              inline BillingAddress: TAddressFrame
                Left = 8
              end
            end

            """;

        Assert.Empty(Errors(EmitFrame("BillingAddress: TAddressFrame;", form)));
        Assert.Equal(["A.form(2,10): error E3101: Form 'Form1': field 'BillingAddress: TAddressFrame' is not declared in class 'TForm1'"],
            Errors(EmitFrame("Other: TAddressFrame;", form)));
        Assert.Equal(["A.form(2,10): error E3102: Form 'Form1': declaration of 'BillingAddress' is not compatible with the form (TAddressFrame expected)"],
            Errors(EmitFrame("BillingAddress: TButton;", form)));
    }

    /// <summary>
    /// LANG-952: the <c>inherited</c> override blocks inside an instance (nested ones too) name components of the frame, so the root
    /// class needs no field for them, not even when it has a field of that name and another type.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-952")]
    public void Inline_OverrideBlocks_NeedNoField()
    {
        const string form = """
            object Form1: TForm1
              inline BillingAddress: TAddressFrame
                inherited Street: TButton
                  Caption = 'Billing street'
                end
                inherited Town: TLabel
                  inherited Inner: TButton
                    Left = 4
                  end
                end
              end
              inline ShippingAddress: TAddressFrame
                inherited Town: TLabel
                  Top = 2
                end
              end
            end

            """;

        var result = EmitFrame("BillingAddress, ShippingAddress: TAddressFrame; Town: TButton;", form);

        Assert.Empty(Errors(result));
        Assert.True(result.Success);
    }

    /// <summary>
    /// LANG-953: the event lines of an instance and of its override blocks are checked against the root class like any event: a
    /// missing handler is E3101 "method", so is a handler declared only in the frame class (it is no method of the root); a handler
    /// that does not fit the visible event property of the instance or of the override block's class is E3102.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-953")]
    public void Inline_EventsAreCheckedAgainstTheRoot()
    {
        const string form = """
            object Form1: TForm1
              inline BillingAddress: TAddressFrame
                OnResize = Clicked
                inherited Street: TButton
                  OnClick = Clicked
                end
                inherited Town: TLabel
                  OnMove = Moved
                  inherited Inner: TButton
                    OnClick = Clicked
                  end
                end
              end
            end

            """;
        const string fields = "BillingAddress: TAddressFrame;";

        Assert.Empty(Errors(EmitFrame(fields, form)));
        Assert.Equal(
            [
                "A.form(3,16): error E3101: Form 'Form1': method 'Missing' is not declared in class 'TForm1'",
                "A.form(5,17): error E3101: Form 'Form1': method 'Missing' is not declared in class 'TForm1'",
                "A.form(10,19): error E3101: Form 'Form1': method 'Missing' is not declared in class 'TForm1'",
            ],
            Errors(EmitFrame(fields, form.Replace("= Clicked", "= Missing", StringComparison.Ordinal))));
        Assert.Equal(["A.form(3,16): error E3102: Form 'Form1': declaration of 'Moved' is not compatible with the form (TNotify expected)"],
            Errors(EmitFrame(fields, form.Replace("OnResize = Clicked", "OnResize = Moved", StringComparison.Ordinal))));
        Assert.Equal(["A.form(5,17): error E3102: Form 'Form1': declaration of 'Moved' is not compatible with the form (TNotify expected)"],
            Errors(EmitFrame(fields, form.ReplaceLineEndings("\n").Replace("Street: TButton\n      OnClick = Clicked", "Street: TButton\n      OnClick = Moved", StringComparison.Ordinal))));
        Assert.Equal(["A.form(8,16): error E3102: Form 'Form1': declaration of 'Clicked' is not compatible with the form (TMove expected)"],
            Errors(EmitFrame(fields, form.Replace("OnMove = Moved", "OnMove = Clicked", StringComparison.Ordinal))));
        Assert.Equal(["A.form(3,16): error E3101: Form 'Form1': method 'FrameOnly' is not declared in class 'TForm1'"],
            Errors(EmitFrame(fields, form.Replace("OnResize = Clicked", "OnResize = FrameOnly", StringComparison.Ordinal))));
    }

    /// <summary>
    /// LANG-954: an <c>object</c> (or <c>inline</c>) inside an instance or inside one of its override blocks adds a component to the
    /// instance; the reader reports that (F0110), the compiler does not: no field and no handler of it is checked.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-954")]
    public void Inline_NestedObject_NoCompilerDiagnostic()
    {
        const string form = """
            object Form1: TForm1
              inline BillingAddress: TAddressFrame
                inherited Street: TButton
                  object Added: TButton
                    OnClick = Missing
                  end
                end
                object Extra: TButton
                  OnClick = Missing
                end
                inline Again: TAddressFrame
                  OnResize = Missing
                end
              end
            end

            """;

        var result = EmitFrame("BillingAddress: TAddressFrame;", form);

        Assert.Empty(Errors(result));
        Assert.True(result.Success);
    }

    /// <summary>
    /// CMP-415 / CMP-350 / ABI-150: the blob is a private byte array, the table an internal constant of { upper-cased name, blob, size },
    /// and <c>$init</c> registers it right after the threadvar descriptor; a unit without an initialization section gets an init entry.
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-415")]
    [Trait("Req", "ABI-150")]
    public void UnitTable_AndRegistration()
    {
        var unit = "unit U;\ninterface\n" + FormTypes + "threadvar T: Integer;\nimplementation\n{$FORM A.form}\n" + FormMethods + "\nend.\n";
        var (result, fs) = Emit("program P;\nuses U;\nbegin end.\n", ("U.pas", unit), ("A.form", "object Form1: TForm1\nend\n"));

        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.ToText())));
        var ir = fs.ReadAllText(@"C:\p\obj\U.ll");
        Assert.Contains("@\"U.$form.0\" = private constant [25 x i8] [i8 111, i8 98, ", ir, StringComparison.Ordinal);
        Assert.Matches(@"@""U\.\$forms"" = internal constant \[1 x \{ ptr, ptr, i64 \}\] \[\{ ptr, ptr, i64 \} \{ ptr getelementptr inbounds \(\{ i32, i32, i16, i16, i32, \[7 x i16\] \}, ptr @\.str\.\d+, i32 0, i32 5\), ptr @""U\.\$form\.0"", i64 25 \}\], align 8", ir);
        Assert.Matches(@"define void @""U\.\$init""\(\) #0 \{\nentry:\n  call void @""System\._TlsRegister""\(ptr @""U\.\$tls""\)\n  call void @""System\._RegisterResources""\(ptr @""U\.\$forms"", i64 1\)\n", ir);
        Assert.Contains("{ ptr, ptr } { ptr @\"U.$init\", ptr null }]", fs.ReadAllText(@"C:\p\obj\P.ll"), StringComparison.Ordinal);   // no init section, still an init entry
    }
}
