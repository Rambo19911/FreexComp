using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Targets;
using static FreexComp.Compiler.Tests.Targets.TargetTestSupport;

namespace FreexComp.Compiler.Tests.Targets;

/// <summary>targets.md CMP-1010 (task C-280): the target group of predefined conditional symbols comes from the descriptor.</summary>
[Trait("Phase", "8")]
[Trait("Kind", "Unit")]
public sealed class TargetSymbolsTests
{
    private static readonly string[] ProductGroup = ["OPC", "OPC_1", "UNICODE"];
    private static readonly string[] Win64Group = ["MSWINDOWS", "WINDOWS", "WIN64", "CPUX64", "CPU64BITS"];
    private static readonly string[] Fake32Group = ["MSWINDOWS", "WINDOWS", "WIN32", "CPUX86", "CPU32BITS"];

    /// <summary>The symbols of <paramref name="candidates"/> that are defined when a file is lexed with <paramref name="options"/>.</summary>
    private static string[] Defined(ParseOptions? options, IEnumerable<string> candidates)
    {
        var names = candidates.ToArray();
        var text = string.Concat(names.Select(n => "{$IFDEF " + n + "} " + n + " {$ENDIF}\n"));
        return [.. Lexer.Tokenize(SourceText.From(@"C:\p\symbols.pas", text), options)
            .Where(t => t.Kind == SyntaxKind.IdentifierToken).Select(t => t.Text)];
    }

    [Fact]
    [Trait("Req", "CMP-1010")]
    public void Win64_PredefinedSymbolsUnchanged()
    {
        string[] all = [.. ProductGroup, .. Win64Group, .. Fake32Group.Except(Win64Group), "ASSERTIONS", "CONSOLE", "LINUX", "CPU386"];
        string[] expected = [.. ProductGroup, .. Win64Group, "ASSERTIONS"];        // ASSERTIONS follows {$C+}, the default

        // without options, with options that name no target, and with the baseline target: the same set as before the card
        Assert.Equal(expected, Defined(null, all));
        Assert.Equal(expected, Defined(new ParseOptions([]), all));
        Assert.Equal(expected, Defined(new ParseOptions([]) { Target = TargetInfo.Win64 }, all));
        Assert.Same(TargetRegistry.Default, new ParseOptions([]).Target);

        // the target group is the descriptor's list, in its order; the product group does not depend on the target
        Assert.Equal(Win64Group, TargetInfo.Win64.PredefinedSymbols);
        Assert.Equal(ProductGroup, PreprocessorContext.ProductSymbols);

        // a compilation: CONSOLE joins by application type (product group)
        const string Program = """
            program P;
            begin
              {$IFDEF WIN64} Writeln(1); {$ENDIF}
              {$IFDEF CPUX64} Writeln(2); {$ENDIF}
              {$IFDEF CPU64BITS} Writeln(3); {$ENDIF}
              {$IFDEF MSWINDOWS} Writeln(4); {$ENDIF}
              {$IFDEF WINDOWS} Writeln(5); {$ENDIF}
              {$IFDEF CONSOLE} Writeln(6); {$ENDIF}
              {$IFDEF WIN32} Writeln(7); {$ENDIF}
              {$IFDEF CPU32BITS} Writeln(8); {$ENDIF}
            end.
            """;
        Assert.Equal([1, 2, 3, 4, 5, 6], Written(EmitLl(TargetInfo.Win64, "P", Program)["P"]));
    }

    [Fact]
    [Trait("Req", "CMP-1010")]
    public void FakeTarget_DefinesItsOwnSymbols()
    {
        string[] all = [.. ProductGroup, .. Win64Group, .. Fake32Group.Except(Win64Group), "ASSERTIONS"];

        // lexer
        Assert.Equal([.. ProductGroup, "MSWINDOWS", "WINDOWS", "WIN32", "CPUX86", "CPU32BITS", "ASSERTIONS"], Defined(new ParseOptions([]) { Target = Fake32 }, all));
        var custom = TargetInfo.Win64 with { Id = "own", PredefinedSymbols = ["ONLYMINE"] };
        Assert.Equal([.. ProductGroup, "ASSERTIONS", "ONLYMINE"], Defined(new ParseOptions([]) { Target = custom }, [.. all, "ONLYMINE"]));

        // compilation: the main file, a used unit and an include file all see the target's group
        const string Program = """
            program P;
            uses U;
            begin
              {$IFDEF WIN64} Writeln(1); {$ENDIF}
              {$IFDEF CPU64BITS} Writeln(2); {$ENDIF}
              {$IFDEF WIN32} Writeln(3); {$ENDIF}
              {$IFDEF CPUX86} Writeln(4); {$ENDIF}
              {$IFDEF CPU32BITS} Writeln(5); {$ENDIF}
              {$IFDEF MSWINDOWS} Writeln(6); {$ENDIF}
              {$I part.inc}
              FromUnit;
            end.
            """;
        const string Unit = """
            unit U;
            interface
            procedure FromUnit;
            implementation
            procedure FromUnit;
            begin
              {$IFDEF CPU32BITS} Writeln(32); {$ELSE} Writeln(64); {$ENDIF}
            end;
            end.
            """;
        const string Include = "{$IFDEF CPUX64} Writeln(7); {$ENDIF} {$IFDEF CPUX86} Writeln(8); {$ENDIF}";
        (string, string)[] files = [(@"C:\p\U.pas", Unit), (@"C:\p\part.inc", Include)];

        var fake = EmitLl(Fake32, "P", Program, files: files);
        Assert.Equal([3, 4, 5, 6, 8], Written(fake["P"]));
        Assert.Equal([32], Written(fake["U"]));

        var win64 = EmitLl(TargetInfo.Win64, "P", Program, files: files);
        Assert.Equal([1, 2, 6, 7], Written(win64["P"]));
        Assert.Equal([64], Written(win64["U"]));

        // the syntax trees of a compilation (what the language service reads) are parsed with the same group
        var fs = new InMemoryFileSystem().Add(Path.Combine(Rtl, "System.pas"), StubSystem).Add(@"C:\p\P.pas", Program).Add(@"C:\p\U.pas", Unit).Add(@"C:\p\part.inc", Include);
        var compilation = Compilation.Create(new CompilationOptions { MainFile = @"C:\p\P.pas", RtlDirectory = Rtl, Target = Fake32 }, [], fs);
        Assert.Empty(compilation.GetDiagnostics(@"C:\p\P.pas", TestContext.Current.CancellationToken));
        var literals = compilation.GetSyntaxTree(@"C:\p\P.pas").Root.DescendantTokens().Where(t => t.Kind == SyntaxKind.IntegerLiteralToken).Select(t => t.Text);
        Assert.Equal(["3", "4", "5", "6", "8"], literals);                       // the include file's tokens are part of the tree
    }

    /// <summary>The integer arguments of the <c>Writeln</c> calls of a module, in order.</summary>
    private static int[] Written(string ll) =>
        [.. Lines(ll).Where(l => l.Contains("call void @\"System._WriteInt\"", StringComparison.Ordinal))
            .Select(l => int.Parse(System.Text.RegularExpressions.Regex.Match(l, "i64 ([0-9]+)").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))];
}
