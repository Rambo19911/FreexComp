using System.Globalization;
using System.Text;
using FreexComp.Compiler.Text;
using FreexComp.Compiler.Toolchain;

namespace FreexComp.Compiler.Tests.Toolchain;

/// <summary>resource-compiler.md §3 (CMP-1300…1309): one script, in-memory file system, no process.</summary>
[Trait("Phase", "8")]
public sealed class ResourceCompilerTests
{
    private const string Bin = @"C:\llvm\bin";
    private const string Obj = @"C:\p\obj\Debug";
    private const string Script = @"C:\p\res\app.rc";
    private const string Output = @"C:\p\obj\Debug\app.res";
    private const string DefaultPreprocessed = "# 1 \"C:\\\\p\\\\res\\\\app.rc\"\n1 RCDATA \"a.bin\"\n";

    private static readonly byte[] Compiled = [1, 2, 3, 4];

    private static InMemoryFileSystem Fs() => new InMemoryFileSystem()
        .Add(Bin + @"\clang.exe", "")
        .Add(Bin + @"\llvm-rc.exe", "")
        .Add(Script, "1 RCDATA \"a.bin\"\n");

    /// <summary>Stands in for both tools: the preprocessor writes its <c>-o</c> file, the resource tool its <c>/fo</c> file.</summary>
    private static FakeProcessRunner Tools(
        InMemoryFileSystem fs, string preprocessed = DefaultPreprocessed, string[]? preprocessorErrors = null, string[]? toolErrors = null) =>
        new((call, _) =>
        {
            if (call.FileName.EndsWith("clang.exe", StringComparison.Ordinal))
            {
                if (preprocessorErrors is not null)
                {
                    return Task.FromResult((1, Array.Empty<string>(), preprocessorErrors));
                }

                fs.Add(call.Arguments[Array.IndexOf(call.Arguments, "-o") + 1], preprocessed);
                return Task.FromResult((0, Array.Empty<string>(), Array.Empty<string>()));
            }

            // the real tool leaves a partial output behind when it fails
            fs.WriteAllBytes(call.Arguments[Array.IndexOf(call.Arguments, "/fo") + 1], toolErrors is null ? Compiled : [0]);
            return Task.FromResult((toolErrors is null ? 0 : 1, Array.Empty<string>(), toolErrors ?? []));
        });

    private static ResourceCompileRequest Request(params string[] includes) => new(Script, Output, Obj) { IncludeDirectories = [.. includes] };

    private static Task<ResourceCompileResult> Compile(
        InMemoryFileSystem fs, FakeProcessRunner tools, ResourceCompileRequest? request = null, Action<string>? commandLines = null) =>
        new ResourceCompiler(new ToolRunner(tools, new ToolRunOptions(Location.ForFile(Script)) { CommandLineSink = commandLines }), new ToolchainPaths(Bin), fs)
            .CompileAsync(request ?? Request(), TestContext.Current.CancellationToken);

    [Fact]
    [Trait("Req", "CMP-1303")]
    [Trait("Req", "CMP-1305")]
    [Trait("Req", "CMP-1309")]
    public async Task CommandLines_PreprocessThenCompile_Golden()
    {
        var fs = Fs();
        var tools = Tools(fs);
        var echoed = new List<string>();
        var request = Request(@"C:\inc\one", @"C:\inc\two") with { Defines = ["DEMO", "LEVEL=3"] };

        var result = await Compile(fs, tools, request, echoed.Add);

        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.ToText())));
        Assert.Equal(2, tools.Calls.Count);
        Assert.Equal(Bin + @"\clang.exe", tools.Calls[0].FileName);
        Assert.Equal(Obj, tools.Calls[0].WorkingDirectory);
        Assert.Equal(
            [
                "-E", "-x", "c", "-w", "-nostdinc", "-DRC_INVOKED", "-DDEMO", "-DLEVEL=3", "-I", @"C:\p\res", "-I", @"C:\inc\one", "-I", @"C:\inc\two",
                "-isystem", Obj + @"\rc-include", "-o", Obj + @"\app.rc.pp", Script,
            ],
            tools.Calls[0].Arguments);
        Assert.Equal(Bin + @"\llvm-rc.exe", tools.Calls[1].FileName);
        Assert.Equal(@"C:\p\res", tools.Calls[1].WorkingDirectory);       // the tool searches its working directory first
        Assert.Equal(
            ["/no-preprocess", "/C", "65001", "/X", "/I", @"C:\p\res", "/I", @"C:\inc\one", "/I", @"C:\inc\two", "/fo", Output + ".tmp", Obj + @"\app.rc.i"],
            tools.Calls[1].Arguments);
        Assert.Equal(2, echoed.Count);                                      // both echoed before they run
        Assert.StartsWith(Bin + @"\clang.exe -E -x c ", echoed[0], StringComparison.Ordinal);
        Assert.StartsWith(Bin + @"\llvm-rc.exe /no-preprocess ", echoed[1], StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "CMP-1304")]
    public async Task LineMarkersAreStrippedAndMapped()
    {
        const string Preprocessed = """
            # 1 "C:\\p\\res\\app.rc"
            # 1 "<built-in>" 1
            # 1 "<built-in>" 3
            # 400 "<built-in>" 3
            # 1 "<command line>" 1
            # 1 "<built-in>" 2
            # 1 "C:\\p\\res\\app.rc" 2
            1 RCDATA "a.bin"
            # 1 "C:\\p\\res\\inc/one.h" 1
            2 RCDATA "one"
            # 1 "C:\\p\\res\\inc\\two.h" 1
            3 RCDATA "two"
            # 3 "C:\\p\\res\\inc/one.h" 2
            4 RCDATA "one again"
            # 3 "C:\\p\\res\\app.rc" 2
            5 RCDATA "last"

            """;
        var fs = Fs();

        var result = await Compile(fs, Tools(fs, Preprocessed.ReplaceLineEndings("\r\n")));

        Assert.True(result.Success);
        Assert.Equal(
            "1 RCDATA \"a.bin\"\n2 RCDATA \"one\"\n3 RCDATA \"two\"\n4 RCDATA \"one again\"\n5 RCDATA \"last\"\n",
            Encoding.UTF8.GetString(fs.ReadAllBytes(Obj + @"\app.rc.i")));
        Assert.Equal(
            [
                new ResourceLineOrigin(Script, 1),
                new ResourceLineOrigin(@"C:\p\res\inc\one.h", 1),
                new ResourceLineOrigin(@"C:\p\res\inc\two.h", 1),
                new ResourceLineOrigin(@"C:\p\res\inc\one.h", 3),
                new ResourceLineOrigin(Script, 3),
            ],
            result.LineMap);
        Assert.Equal([Script, @"C:\p\res\inc\one.h", @"C:\p\res\inc\two.h"], result.Dependencies);
    }

    [Fact]
    [Trait("Req", "CMP-1303")]
    public async Task PreprocessorError_E3111_AtTheOriginalPosition()
    {
        // a missing include inside an included file (output of the pinned preprocessor)
        var fs = Fs();
        var tools = Tools(fs, preprocessorErrors:
        [
            @"In file included from C:\p\res\app.rc:2:",
            @"C:\p\res\inc/bad.h:2:10: fatal error: 'missing.h' file not found",
            "    2 | #include \"missing.h\"",
            "      |          ^~~~~~~~~~~",
            "1 error generated.",
        ]);

        var result = await Compile(fs, tools);

        Assert.False(result.Success);
        Assert.Equal([@"C:\p\res\inc\bad.h(2,10): error E3111: Resource script: 'missing.h' file not found"], result.Diagnostics.Select(d => d.ToText()));
        Assert.Single(tools.Calls);                                         // step 2 is not run

        // #error and a second error of the same run
        fs = Fs();
        result = await Compile(fs, Tools(fs, preprocessorErrors:
        [
            @"C:\p\res\app.rc:3:4: error: stop here",
            "    3 |   #error stop here",
            @"C:\p\res\app.rc:4:2: error: unterminated conditional directive",
            "2 errors generated.",
        ]));

        Assert.Equal(
            [
                @"C:\p\res\app.rc(3,4): error E3111: Resource script: stop here",
                @"C:\p\res\app.rc(4,2): error E3111: Resource script: unterminated conditional directive",
            ],
            result.Diagnostics.Select(d => d.ToText()));

        // no line of the known form: the first output line, at the script
        fs = Fs();
        result = await Compile(fs, Tools(fs, preprocessorErrors: ["clang: error: something else"]));

        Assert.Equal([@"C:\p\res\app.rc(0,0): error E3111: Resource script: clang: error: something else"], result.Diagnostics.Select(d => d.ToText()));
    }

    /// <summary>The three outputs recorded by <c>ResourceToolFactsTests.PinnedToolBehaviour</c>: the tool names no line, so the position is (0,0).</summary>
    [Theory]
    [InlineData("llvm-rc: Error parsing file: expected '{', got ,", null, "Error parsing file: expected '{', got ,")]
    [InlineData("llvm-rc: Error parsing file: expected int or string, got <EOF>", null, "Error parsing file: expected int or string, got <EOF>")]
    [InlineData("llvm-rc: Error in ICON statement (ID 1): ", "error : file not found : nothere.ico", "Error in ICON statement (ID 1): file not found : nothere.ico")]
    [Trait("Req", "CMP-1306")]
    public async Task ToolError_E3112_PositionMappedOrZero(string first, string? second, string message)
    {
        var fs = Fs();

        var result = await Compile(fs, Tools(fs, toolErrors: second is null ? ["a note of the tool", first] : ["a note of the tool", first, second]));

        Assert.False(result.Success);
        Assert.Equal([@"C:\p\res\app.rc(0,0): error E3112: Resource script error: " + message], result.Diagnostics.Select(d => d.ToText()));
    }

    /// <summary>
    /// C-348: the tool names no line for a syntax error, so the compiler runs it again on prefixes of the top-level
    /// statements (bisection) and reports the first statement whose prefix fails, at its line in the script or header.
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-1306")]
    public async Task SyntaxError_ReportsTheStatementLine()
    {
        const string Preprocessed = """
            # 1 "C:\\p\\res\\app.rc"
            1 RCDATA "a.bin"

            2 RCDATA
            BEGIN
              1, 2
            END
            # 1 "C:\\p\\res\\inc.h" 1
            3 DIALOG 0, 0, 10, 10
            STYLE 0
            CAPTION "BEGIN"
            BEGIN
              PUSHBUTTON "END", 1, 0, 0, 1, 1
            END
            # 6 "C:\\p\\res\\app.rc" 2
            LANGUAGE 9, 1
            4 RCDATA BEGIN 1 END   5 RCDATA BEGIN {0} END
            STRINGTABLE
            BEGIN
              1 "x"
            END
            6 ICON icon.ico

            """;
        const string ParseError = "llvm-rc: Error parsing file: expected int or string, got BAD";

        // the bad text in statement 5 (script line 7, column of the "5"), in the dialog of the header, in the string table
        foreach (var (bad, expected) in new[]
        {
            (Preprocessed.Replace("{0}", "BAD", StringComparison.Ordinal), @"C:\p\res\app.rc(7,24)"),
            (Preprocessed.Replace("{0}", "1", StringComparison.Ordinal).Replace("STYLE 0", "STYLE BAD", StringComparison.Ordinal), @"C:\p\res\inc.h(1,1)"),
            (Preprocessed.Replace("{0}", "1", StringComparison.Ordinal).Replace("1 \"x\"", "BAD \"x\"", StringComparison.Ordinal), @"C:\p\res\app.rc(8,1)"),
        })
        {
            var fs = Fs();
            var inputs = new List<string>();
            var tools = new FakeProcessRunner((call, _) =>
            {
                if (call.FileName.EndsWith("clang.exe", StringComparison.Ordinal))
                {
                    fs.Add(call.Arguments[Array.IndexOf(call.Arguments, "-o") + 1], bad.ReplaceLineEndings("\r\n"));
                    return Task.FromResult((0, Array.Empty<string>(), Array.Empty<string>()));
                }

                var input = Encoding.UTF8.GetString(fs.ReadAllBytes(call.Arguments[^1]));
                inputs.Add(input);
                var fails = input.Contains("BAD", StringComparison.Ordinal);
                fs.WriteAllBytes(call.Arguments[Array.IndexOf(call.Arguments, "/fo") + 1], fails ? [0] : Compiled);
                return Task.FromResult((fails ? 1 : 0, Array.Empty<string>(), fails ? new[] { ParseError } : []));
            });

            var result = await Compile(fs, tools);

            Assert.False(result.Success);
            Assert.Equal([expected + ": error E3112: Resource script error: Error parsing file: expected int or string, got BAD"], result.Diagnostics.Select(d => d.ToText()));

            // 8 top-level statements: at most ceil(log2 8) + 1 = 4 runs after the first, each on a prefix of whole statements
            var full = Encoding.UTF8.GetString(fs.ReadAllBytes(Obj + @"\app.rc.i"));
            Assert.Equal(full, inputs[0]);
            Assert.InRange(inputs.Count - 1, 1, 4);
            Assert.All(inputs.Skip(1), prefix => Assert.True(
                prefix.Length == 0 || (full.StartsWith(prefix[..^1], StringComparison.Ordinal) && prefix[^1] == '\n' && prefix[^2] is 'D' or 'o' or '1' or '"'),
                "a prefix of whole statements: " + prefix));
            Assert.False(fs.FileExists(Obj + @"\app.rc.part.i"));            // the bisection input is removed
            Assert.False(fs.FileExists(Output + ".tmp"));
        }

        // the error lies in a statement that runs to the end of the input: no statement ends after it, so (0,0) as before
        var eof = Fs();
        var eofResult = await Compile(eof, new FakeProcessRunner((call, _) =>
        {
            if (call.FileName.EndsWith("clang.exe", StringComparison.Ordinal))
            {
                eof.Add(call.Arguments[Array.IndexOf(call.Arguments, "-o") + 1], "# 1 \"C:\\\\p\\\\res\\\\app.rc\"\n1 RCDATA BEGIN 1 END\n2 RCDATA\nBEGIN\n  1, 2\n");
                return Task.FromResult((0, Array.Empty<string>(), Array.Empty<string>()));
            }

            var unfinished = Encoding.UTF8.GetString(eof.ReadAllBytes(call.Arguments[^1])).Contains("2 RCDATA", StringComparison.Ordinal);
            return Task.FromResult((unfinished ? 1 : 0, Array.Empty<string>(), unfinished ? new[] { "llvm-rc: Error parsing file: expected int or string, got <EOF>" } : []));
        }));

        Assert.Equal(
            [@"C:\p\res\app.rc(0,0): error E3112: Resource script error: Error parsing file: expected int or string, got <EOF>"],
            eofResult.Diagnostics.Select(d => d.ToText()));
    }

    /// <summary>
    /// C-349: the language constants of <c>rcdefs.h</c> against the Open Specifications document [MS-LCID] (a sample of its LCID
    /// table, written here from the document: the primary language is the low 10 bits, the sublanguage the high 6 bits), and
    /// <c>MAKELANGID</c> evaluated from its definition with the operators the resource tool accepts.
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-1328")]
    public void RcDefs_LanguageConstantsMatchTheLcidTable()
    {
        var header = Encoding.UTF8.GetString(ResourceHeaders.Files.Single(f => f.Name == "rcdefs.h").Content).Split('\n');
        Assert.Contains("[MS-LCID]", header[0], StringComparison.Ordinal);
        var defines = header.Where(l => l.StartsWith("#define ", StringComparison.Ordinal) && !l.Contains('(', StringComparison.Ordinal))
            .Select(l => l.Split(' '))
            .Where(p => p[1].StartsWith("LANG_", StringComparison.Ordinal) || p[1].StartsWith("SUBLANG_", StringComparison.Ordinal))
            .ToDictionary(p => p[1], p => Evaluate(p[2]), StringComparer.Ordinal);

        (string Primary, string Sub, int Lcid)[] sample =
        [
            ("NEUTRAL", "NEUTRAL", 0x0000), ("NEUTRAL", "DEFAULT", 0x0400), ("NEUTRAL", "SYS_DEFAULT", 0x0800),
            ("ENGLISH", "ENGLISH_US", 0x0409), ("ENGLISH", "ENGLISH_UK", 0x0809), ("ENGLISH", "ENGLISH_AUS", 0x0C09), ("ENGLISH", "ENGLISH_CAN", 0x1009),
            ("GERMAN", "GERMAN", 0x0407), ("GERMAN", "GERMAN_SWISS", 0x0807), ("GERMAN", "GERMAN_AUSTRIAN", 0x0C07),
            ("FRENCH", "FRENCH", 0x040C), ("FRENCH", "FRENCH_BELGIAN", 0x080C), ("FRENCH", "FRENCH_CANADIAN", 0x0C0C),
            ("SPANISH", "SPANISH", 0x040A), ("SPANISH", "SPANISH_MEXICAN", 0x080A), ("SPANISH", "SPANISH_MODERN", 0x0C0A), ("SPANISH", "SPANISH_US", 0x540A),
            ("ITALIAN", "ITALIAN", 0x0410), ("JAPANESE", "JAPANESE_JAPAN", 0x0411), ("KOREAN", "KOREAN", 0x0412),
            ("CHINESE", "CHINESE_TRADITIONAL", 0x0404), ("CHINESE", "CHINESE_SIMPLIFIED", 0x0804), ("CHINESE", "CHINESE_HONGKONG", 0x0C04),
            ("RUSSIAN", "RUSSIAN_RUSSIA", 0x0419), ("LATVIAN", "LATVIAN_LATVIA", 0x0426), ("LITHUANIAN", "LITHUANIAN", 0x0427),
            ("ESTONIAN", "ESTONIAN_ESTONIA", 0x0425), ("POLISH", "POLISH_POLAND", 0x0415), ("PORTUGUESE", "PORTUGUESE_BRAZILIAN", 0x0416),
            ("PORTUGUESE", "PORTUGUESE", 0x0816), ("DUTCH", "DUTCH_BELGIAN", 0x0813), ("SWEDISH", "SWEDISH_FINLAND", 0x081D),
            ("NORWEGIAN", "NORWEGIAN_NYNORSK", 0x0814), ("SERBIAN", "SERBIAN_SERBIA_LATIN", 0x241A), ("CROATIAN", "CROATIAN_CROATIA", 0x041A),
            ("ARABIC", "ARABIC_QATAR", 0x4001), ("UKRAINIAN", "UKRAINIAN_UKRAINE", 0x0422), ("TURKISH", "TURKISH_TURKEY", 0x041F),
        ];

        const string Macro = "#define MAKELANGID(p, s) ";
        var body = Assert.Single(header, l => l.StartsWith(Macro, StringComparison.Ordinal))[Macro.Length..];
        foreach (var (primary, sub, lcid) in sample)
        {
            Assert.Equal(lcid & 0x3FF, defines["LANG_" + primary]);
            Assert.Equal(lcid >> 10, defines["SUBLANG_" + sub]);
            var expanded = body
                .Replace("(p)", "(" + defines["LANG_" + primary].ToString(CultureInfo.InvariantCulture) + ")", StringComparison.Ordinal)
                .Replace("(s)", "(" + defines["SUBLANG_" + sub].ToString(CultureInfo.InvariantCulture) + ")", StringComparison.Ordinal);
            Assert.Equal(lcid, Evaluate(expanded));
        }

        // every primary id fits in 10 bits, every sublanguage in 6
        Assert.All(defines.Where(d => d.Key.StartsWith("LANG_", StringComparison.Ordinal)), d => Assert.InRange(d.Value, 0, 0x3FF));
        Assert.All(defines.Where(d => d.Key.StartsWith("SUBLANG_", StringComparison.Ordinal)), d => Assert.InRange(d.Value, 0, 0x3F));
        Assert.True(defines.Keys.Count(k => k.StartsWith("LANG_", StringComparison.Ordinal)) >= 100);
    }

    /// <summary>Numbers (decimal or <c>0x</c>), parentheses, <c>*</c> and <c>|</c>: expression forms the resource tool reads.</summary>
    private static int Evaluate(string expression)
    {
        var position = 0;
        var result = Or();
        Skip();
        Assert.Equal(expression.Length, position);
        return result;

        int Or()
        {
            var value = Product();
            while (Next('|'))
            {
                value |= Product();
            }

            return value;
        }

        int Product()
        {
            var value = Primary();
            while (Next('*'))
            {
                value *= Primary();
            }

            return value;
        }

        int Primary()
        {
            if (Next('('))
            {
                var inner = Or();
                Assert.True(Next(')'), "')' expected in " + expression);
                return inner;
            }

            Skip();
            var start = position;
            while (position < expression.Length && (char.IsAsciiHexDigit(expression[position]) || expression[position] is 'x' or 'X'))
            {
                position++;
            }

            var text = expression[start..position];
            return text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? int.Parse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
                : int.Parse(text, CultureInfo.InvariantCulture);
        }

        bool Next(char c)
        {
            Skip();
            if (position < expression.Length && expression[position] == c)
            {
                position++;
                return true;
            }

            return false;
        }

        void Skip()
        {
            while (position < expression.Length && expression[position] == ' ')
            {
                position++;
            }
        }
    }

    [Fact]
    [Trait("Req", "CMP-1306")]
    public async Task ToolError_WithoutAnErrorLine_IsE4004WithTheOutput()
    {
        var fs = Fs();

        var result = await Compile(fs, Tools(fs, toolErrors: ["something unexpected"]));

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("E4004", diagnostic.Code);
        Assert.Equal("llvm-rc.exe failed with exit code 1", diagnostic.Message);
        Assert.Equal("something unexpected", Assert.Single(diagnostic.Related).Message);
    }

    [Fact]
    [Trait("Req", "CMP-1300")]
    public async Task MissingTool_E4001_NamesTheResourceTool()
    {
        var fs = Fs();
        fs.Delete(Bin + @"\llvm-rc.exe");
        var tools = Tools(fs);

        var result = await Compile(fs, tools);

        Assert.False(result.Success);
        Assert.Equal([@"C:\p\res\app.rc(0,0): error E4001: Toolchain not found: llvm-rc.exe (searched: C:\llvm\bin)"], result.Diagnostics.Select(d => d.ToText()));
        Assert.Empty(tools.Calls);                                          // nothing is compiled
        Assert.DoesNotContain("llvm-rc.exe", ToolchainLocator.RequiredTools);
    }

    [Fact]
    [Trait("Req", "CMP-1301")]
    public async Task MissingOrUtf16Script_E3110_E3113()
    {
        var fs = Fs();
        fs.Delete(Script);
        var tools = Tools(fs);
        var missing = await Compile(fs, tools);
        Assert.Equal([@"C:\p\res\app.rc(0,0): error E3110: Resource script 'C:\p\res\app.rc' not found or unreadable"], missing.Diagnostics.Select(d => d.ToText()));

        foreach (var mark in new byte[][] { [0xFF, 0xFE], [0xFE, 0xFF], [0xFF, 0xFE, 0x00, 0x00], [0x00, 0x00, 0xFE, 0xFF] })
        {
            fs.WriteAllBytes(Script, [.. mark, (byte)'1', 0]);
            var utf16 = await Compile(fs, tools);
            Assert.Equal([@"C:\p\res\app.rc(0,0): error E3113: Resource script 'C:\p\res\app.rc' is not UTF-8 text"], utf16.Diagnostics.Select(d => d.ToText()));
        }

        Assert.Empty(tools.Calls);

        fs.WriteAllBytes(Script, [0xEF, 0xBB, 0xBF, (byte)'1']);           // UTF-8 with a byte order mark is accepted
        Assert.True((await Compile(fs, tools)).Success);
    }

    [Fact]
    [Trait("Req", "CMP-1305")]
    public async Task FailureLeavesNoOutput_SuccessReplacesAtomically()
    {
        var fs = Fs().Add(Output, "previous");

        var ok = await Compile(fs, Tools(fs));

        Assert.True(ok.Success);
        Assert.Equal(Compiled, fs.ReadAllBytes(Output));
        Assert.False(fs.FileExists(Output + ".tmp"));

        var failed = await Compile(fs, Tools(fs, toolErrors: ["llvm-rc: Error parsing file: expected int or string, got <EOF>"]));

        Assert.False(failed.Success);
        Assert.False(fs.FileExists(Output));                                // neither the previous output nor the partial one
        Assert.False(fs.FileExists(Output + ".tmp"));

        fs.Add(Output, "previous");
        Assert.False((await Compile(fs, Tools(fs, preprocessorErrors: ["x.rc:1:1: error: e"]))).Success);
        Assert.False(fs.FileExists(Output));
    }

    [Fact]
    [Trait("Req", "CMP-1309")]
    public async Task Cancellation_RemovesTheTemporaryOutput()
    {
        var fs = Fs();
        using var cancel = new CancellationTokenSource();
        var tools = new FakeProcessRunner((call, _) =>
        {
            if (call.FileName.EndsWith("clang.exe", StringComparison.Ordinal))
            {
                fs.Add(call.Arguments[Array.IndexOf(call.Arguments, "-o") + 1], DefaultPreprocessed);
                return Task.FromResult((0, Array.Empty<string>(), Array.Empty<string>()));
            }

            fs.WriteAllBytes(Output + ".tmp", [0]);
            cancel.Cancel();
            return Task.FromCanceled<(int, string[], string[])>(cancel.Token);
        });
        var compiler = new ResourceCompiler(new ToolRunner(tools, new ToolRunOptions(Location.ForFile(Script))), new ToolchainPaths(Bin), fs);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => compiler.CompileAsync(Request(), cancel.Token));

        Assert.False(fs.FileExists(Output + ".tmp"));
        Assert.False(fs.FileExists(Output));
    }

    [Fact]
    [Trait("Req", "CMP-1302")]
    [Trait("Req", "CMP-1329")]
    public async Task OwnHeadersAreWrittenOnce_AndSearchedLast()
    {
        var fs = Fs();
        var tools = Tools(fs);

        Assert.True((await Compile(fs, tools, Request(@"C:\inc"))).Success);

        var directory = Obj + @"\rc-include";
        Assert.Equal(ResourceHeaders.Files.Select(f => f.Name), fs.EnumerateFiles(directory, "*").Select(Path.GetFileName).Order(StringComparer.Ordinal));
        foreach (var (name, content) in ResourceHeaders.Files)
        {
            Assert.Equal(content, fs.ReadAllBytes(Path.Combine(directory, name)));
        }

        var times = ResourceHeaders.Files.Select(f => fs.GetLastWriteTimeUtc(Path.Combine(directory, f.Name))).ToList();
        fs.Add(directory + @"\winver.h", "changed");
        var changed = fs.GetLastWriteTimeUtc(directory + @"\winver.h");

        Assert.True((await Compile(fs, tools, Request(@"C:\inc"))).Success);

        for (var i = 0; i < times.Count; i++)
        {
            var name = ResourceHeaders.Files[i].Name;
            var now = fs.GetLastWriteTimeUtc(Path.Combine(directory, name));
            if (name == "winver.h")
            {
                Assert.NotEqual(changed, now);                              // other bytes: written again
                Assert.Equal(ResourceHeaders.Files[i].Content, fs.ReadAllBytes(Path.Combine(directory, name)));
            }
            else
            {
                Assert.Equal(times[i], now);                                // matching bytes: not rewritten
            }
        }

        // the own directory is the last include directory, so a user's file of the same name wins
        var args = tools.Calls[0].Arguments;
        var own = Array.IndexOf(args, "-isystem");
        Assert.Equal(directory, args[own + 1]);
        Assert.True(own > Array.LastIndexOf(args, "-I"));
        Assert.DoesNotContain(directory, tools.Calls[1].Arguments);
    }

    [Fact]
    [Trait("Req", "CMP-1307")]
    public async Task Dependencies_ScriptIncludesAndDataFiles()
    {
        const string Preprocessed = """
            # 1 "C:\\p\\res\\app.rc"
            # 1 "C:\\inc\\ids.h" 1
            # 2 "C:\\p\\res\\app.rc" 2
            1 ICON "app.ico"
            2 BITMAP logo.bmp
            3 RCDATA "sub\\data.bin"
            4 RCDATA "shared.bin"
            5 RCDATA "C:\\abs\\x.bin"
            STRINGTABLE BEGIN 1 "Hello" 2 "no such file.txt" END

            """;
        var fs = Fs()
            .Add(@"C:\p\res\app.ico", "i")
            .Add(@"C:\p\res\logo.bmp", "b")
            .Add(@"C:\p\res\sub\data.bin", "d")
            .Add(@"C:\inc\shared.bin", "s")
            .Add(@"C:\inc\app.ico", "the script's directory is searched first")
            .Add(@"C:\abs\x.bin", "x")
            .Add(@"C:\inc\ids.h", "");

        var result = await Compile(fs, Tools(fs, Preprocessed), Request(@"C:\inc"));

        Assert.True(result.Success);
        Assert.Equal(
            [Script, @"C:\inc\ids.h", @"C:\p\res\app.ico", @"C:\p\res\logo.bmp", @"C:\p\res\sub\data.bin", @"C:\inc\shared.bin", @"C:\abs\x.bin"],
            result.Dependencies);
    }
}
