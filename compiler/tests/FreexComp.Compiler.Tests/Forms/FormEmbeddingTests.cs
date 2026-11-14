using FreexComp.Compiler.Tests.Emit;

namespace FreexComp.Compiler.Tests.Forms;

/// <summary>C-114: <c>{$FORM}</c> through <see cref="Compilation.Emit"/> — E3100…E3104, F0001 pass-through, the CMP-415 table and its registration.</summary>
[Trait("Phase", "2")]
public sealed class FormEmbeddingTests
{
    private static (EmitResult Result, InMemoryFileSystem Fs) Emit(string main, params (string Name, string Text)[] files)
    {
        var fs = new InMemoryFileSystem().Add(@"C:\p\P.pas", main)
            .Add(@"C:\rtl\System.pas", File.ReadAllText(Path.Combine(MiniRtlCompilesTests.MiniRtlDirectory, "System.pas")));
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
