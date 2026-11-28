using System.Text.RegularExpressions;
using FreexComp.Compiler;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;

namespace FreexComp.Rtl.Tests.Harness;

/// <summary>
/// Structural rules of the RTL sources (rtl/README.md sections 1-4, sysutils.md RTL-201, winapi.md RTL-800/801/814/834),
/// checked on the parsed units of <c>rtl/src</c> and <c>rtl/winapi/src</c> (syntax only, <c>FreexComp.Compiler</c>).
/// </summary>
[Trait("Phase", "2")]
[Trait("Category", "Integration")]
public sealed partial class RtlUnitRulesTests
{
    public static string SourceDirectory => Path.Combine(RtlSetup.RepositoryRoot, "rtl", "src");

    public static string WinApiDirectory => Path.Combine(RtlSetup.RepositoryRoot, "rtl", "winapi", "src");

    /// <summary>RTL-004: the units ui-framework needs at milestone M2.5.</summary>
    public static readonly string[] M25Units = ["Classes", "TypInfo", "SyncObjs", "Types", "Math", "SysUtils"];

    /// <summary>The RTL units by canonical name (file name without extension).</summary>
    public static Dictionary<string, (string Path, UnitSyntax Unit)> Units()
    {
        var units = new Dictionary<string, (string, UnitSyntax)>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.GetFiles(SourceDirectory, "*.pas").Concat(Directory.GetFiles(WinApiDirectory, "*.pas"))
                     .Order(StringComparer.Ordinal))
        {
            units.Add(Path.GetFileNameWithoutExtension(path), (path, Parse(path)));
        }

        return units;
    }

    public static UnitSyntax Parse(string path)
    {
        var tree = SyntaxTree.Parse(SourceText.From(path, File.ReadAllText(path)), null, PhysicalFileSystem.Instance);
        return Assert.IsType<UnitSyntax>(tree.Root);
    }

    /// <summary>The units named by the interface and implementation uses clauses.</summary>
    public static List<string> Uses(UnitSyntax unit) =>
    [
        .. new[] { unit.InterfaceSection.UsesClause, unit.ImplementationSection.UsesClause }
            .Where(c => c is not null)
            .SelectMany(c => c!.Units)
            .Select(u => u.Name.ToString().Trim()),
    ];

    /// <summary>
    /// RTL-001: canonical unit names are unscoped (the unit name is the file name; the only dotted names are
    /// <c>Generics.Defaults</c> and <c>Generics.Collections</c>), and no unit exists twice (once per canonical name, no
    /// file under a scoped alias name: scoped names come only from <c>unit-aliases.json</c>).
    /// </summary>
    [Fact]
    [Trait("Req", "RTL-001")]
    public void Units_HaveCanonicalUnscopedNames()
    {
        var problems = new List<string>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var aliases = UnitAliasTests.Validate(File.ReadAllBytes(UnitAliasTests.TablePath), problems).Select(a => a.Alias)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (file, (path, unit)) in Units())
        {
            var declared = unit.Name.ToString().Trim();
            if (!string.Equals(declared, file, StringComparison.Ordinal))
            {
                problems.Add($"{path}: declares unit '{declared}'");
            }

            if (file.Contains('.') && file is not ("Generics.Defaults" or "Generics.Collections"))
            {
                problems.Add($"{path}: dotted canonical name");
            }

            if (aliases.Contains(file))
            {
                problems.Add($"{path}: a source file under a scoped alias name");
            }

            if (!names.Add(file))
            {
                problems.Add($"{path}: unit '{file}' exists twice");
            }
        }

        Assert.Contains("SysUtils", names);
        Assert.Contains("Generics.Collections", names);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>
    /// RTL-008: nothing in <c>rtl/</c> refers to the compiler area's throw-away mini RTL (no path, unit search directory
    /// or project reference names it); the RTL is written from its specification only. This file is the one exception:
    /// it holds the patterns.
    /// </summary>
    [Fact]
    [Trait("Req", "RTL-008")]
    public void Rtl_DoesNotReferToTheMiniRtl()
    {
        var self = Path.Combine(RtlSetup.TestsDirectory, "Harness", "RtlUnitRulesTests.cs");
        var root = Path.Combine(RtlSetup.RepositoryRoot, "rtl");
        var problems = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file);
            var parts = relative.Split(Path.DirectorySeparatorChar);
            if (parts.Contains("bin", StringComparer.OrdinalIgnoreCase) || parts.Contains("obj", StringComparer.OrdinalIgnoreCase)
                || string.Equals(Path.GetFullPath(file), Path.GetFullPath(self), StringComparison.OrdinalIgnoreCase)
                || !TextExtensions.Contains(Path.GetExtension(file)))
            {
                continue;
            }

            if (MiniRtlName().IsMatch(File.ReadAllText(file)))
            {
                problems.Add(relative);
            }
        }

        Assert.True(problems.Count == 0, "refers to the mini RTL: " + string.Join(", ", problems));
        Assert.Matches(MiniRtlName(), "-U compiler/tests/minirtl");
        Assert.Matches(MiniRtlName(), "the Mini RTL");
    }

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".pas", ".inc", ".def", ".cs", ".csproj", ".props", ".targets", ".json", ".txt", ".form" };

    [GeneratedRegex(@"mini[-_ ]?rtl", RegexOptions.IgnoreCase)]
    private static partial Regex MiniRtlName();

    /// <summary>
    /// RTL-002: no RTL unit imports anything outside <c>rtl/</c> (every used unit is an RTL unit). The other half, the
    /// external symbols, is <see cref="DefFileTests.DefFilesMatchDeclarations"/> (every <c>external</c> has an entry in
    /// one of the Win32 <c>.def</c> files, so no C runtime function can be imported).
    /// </summary>
    [Fact]
    [Trait("Req", "RTL-002")]
    public void Units_UseOnlyRtlUnits()
    {
        var units = Units();
        var problems = units.SelectMany(u => Uses(u.Value.Unit).Where(n => !units.ContainsKey(n)).Select(n => $"{u.Key} uses '{n}'"))
            .ToList();
        Assert.True(problems.Count == 0, string.Join("\n", problems));

        var dlls = Directory.GetFiles(DefFileTests.DefDirectory, "*.def").Select(Path.GetFileNameWithoutExtension).ToList();
        Assert.DoesNotContain(dlls, d => d!.StartsWith("msvc", StringComparison.OrdinalIgnoreCase)
            || d!.StartsWith("ucrt", StringComparison.OrdinalIgnoreCase) || d!.StartsWith("api-ms-win-crt", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>RTL-004: the M2.5 units use no generics, anonymous methods, method references or helpers, also not through a used unit.</summary>
    [Fact]
    [Trait("Req", "RTL-004")]
    public void M25Units_UseNoM26Features()
    {
        var units = Units();
        var problems = new List<string>();
        foreach (var name in M25Units)
        {
            var (_, unit) = units[name];
            foreach (var node in unit.DescendantNodes())
            {
                // classes.md section 4 (TStrings table) declares ToStringArray: TArray<string> "when M2.6 exists" (R-196):
                // the one instantiation the spec itself places in an M2.5 unit
                if (name == "Classes" && node.AncestorsAndSelf().OfType<GenericNameSyntax>().Any(g => g.ToString().Trim() == "TArray<string>"))
                {
                    continue;
                }

                var feature = node switch
                {
                    TypeParameterListSyntax => "a generic declaration",
                    TypeArgumentListSyntax or GenericNameSyntax => "a generic instantiation",
                    AnonymousMethodExpressionSyntax => "an anonymous method",
                    ProceduralTypeSyntax { ReferenceKeyword: not null } => "a method reference type",
                    HelperTypeSyntax => "a helper",
                    _ => null,
                };
                if (feature is not null)
                {
                    problems.Add($"{name}: {feature}");
                }
            }

            problems.AddRange(Closure(units, name).Where(u => !M25Units.Contains(u, StringComparer.OrdinalIgnoreCase)
                && units[u].Unit.DescendantNodes().Any(n => n is TypeParameterListSyntax or HelperTypeSyntax or AnonymousMethodExpressionSyntax))
                .Select(u => $"{name}: uses '{u}', which needs M2.6"));
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems.Distinct()));
    }

    /// <summary>
    /// RTL-005: <c>System</c> uses no unit, <c>SysUtils</c> does not use <c>Classes</c> and <c>Windows</c> does not use
    /// <c>SysUtils</c> (not even indirectly), and the graph of interface + implementation uses is acyclic.
    /// </summary>
    [Fact]
    [Trait("Req", "RTL-005")]
    [Trait("Req", "RTL-801")]
    public void Units_FollowTheDependencyRules()
    {
        var units = Units();
        Assert.Empty(Uses(units["System"].Unit));
        Assert.DoesNotContain("Classes", Closure(units, "SysUtils"), StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("SysUtils", Closure(units, "Windows"), StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Windows", Closure(units, "System"), StringComparer.OrdinalIgnoreCase);
        var cycles = units.Keys.Where(u => Closure(units, u).Contains(u, StringComparer.OrdinalIgnoreCase)).ToList();
        Assert.True(cycles.Count == 0, "units on a uses cycle: " + string.Join(", ", cycles));
    }

    /// <summary>Every unit reachable from <paramref name="start"/> through uses clauses (not including it unless on a cycle).</summary>
    public static HashSet<string> Closure(IReadOnlyDictionary<string, (string Path, UnitSyntax Unit)> units, string start)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var work = new Stack<string>(Uses(units[start].Unit));
        while (work.Count > 0)
        {
            var next = work.Pop();
            if (seen.Add(next) && units.TryGetValue(next, out var u))
            {
                foreach (var used in Uses(u.Unit))
                {
                    work.Push(used);
                }
            }
        }

        return seen;
    }

    [GeneratedRegex(@"\{[^}]*\}|\(\*.*?\*\)|//[^\n]*", RegexOptions.Singleline)]
    private static partial Regex Comment();

    [GeneratedRegex(@"\.Create(Fmt)?\(\s*'", RegexOptions.IgnoreCase)]
    private static partial Regex LiteralMessage();

    [GeneratedRegex(@"\bresourcestring\b", RegexOptions.IgnoreCase)]
    private static partial Regex ResourceString();

    /// <summary>
    /// RTL-201: the RTL's message strings live in <c>RTLConsts</c>, in <c>resourcestring</c> sections: no other unit
    /// declares a resourcestring, and no unit creates an exception with a literal message.
    /// </summary>
    [Fact]
    [Trait("Req", "RTL-201")]
    [Trait("Req", "RTL-650")]
    public void Messages_LiveInRtlConstsResourceStrings()
    {
        var problems = new List<string>();
        foreach (var (name, (path, _)) in Units())
        {
            var code = Comment().Replace(File.ReadAllText(path), " ");
            var resourceStrings = ResourceString().Matches(code).Count;
            if (name == "RTLConsts")
            {
                Assert.True(resourceStrings > 0, "RTLConsts has no resourcestring section");
                Assert.DoesNotMatch(@"(?i)\bconst\b", code);
                continue;
            }

            if (resourceStrings > 0)
            {
                problems.Add($"{name}: declares a resourcestring section (message strings belong in RTLConsts)");
            }

            foreach (var line in code.Split('\n').Where(l => LiteralMessage().IsMatch(l)))
            {
                problems.Add($"{name}: literal message: {line.Trim()}");
            }
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>
    /// RTL-800: each binding unit starts with a comment naming the reference pages it was written from. The comment follows
    /// the licence line, which is line 1 of every runtime source file (LIC-050).
    /// </summary>
    [Fact]
    [Trait("Req", "RTL-800")]
    public void Bindings_NameTheirReferencePages()
    {
        foreach (var path in Directory.GetFiles(WinApiDirectory, "*.pas"))
        {
            const string licence = "// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception\n";
            var text = File.ReadAllText(path).TrimStart('\uFEFF').Replace("\r\n", "\n");
            Assert.True(text.StartsWith(licence, StringComparison.Ordinal), Path.GetFileName(path) + ": line 1 is not the licence line");
            text = text[licence.Length..];
            Assert.True(text.StartsWith('{'), Path.GetFileName(path) + ": line 2 does not start the descriptive comment");
            var header = text[..(text.IndexOf('}') + 1)];
            Assert.True(header.Contains("learn.microsoft.com", StringComparison.Ordinal), Path.GetFileName(path) + ": the header names no reference");
            Assert.True(header.Contains("reference page", StringComparison.Ordinal) || header.Contains("pages", StringComparison.Ordinal),
                Path.GetFileName(path) + ": the header names no reference pages");
        }
    }

    /// <summary>
    /// RTL-801: <c>System</c> declares its kernel32 imports privately (implementation section only) with the signatures of
    /// the <c>Windows</c> declarations of the same export: same routine kind, parameter count, parameter modes and types
    /// (after resolving the type aliases of <c>Windows</c>; a typed pointer or callback type is <c>Pointer</c>), and result type.
    /// </summary>
    [Fact]
    [Trait("Req", "RTL-801")]
    public void System_RedeclaresKernel32ImportsPrivately()
    {
        var units = Units();
        var system = units["System"].Unit;
        var windows = units["Windows"].Unit;
        Assert.Empty(Externals(system.InterfaceSection));

        var aliases = windows.InterfaceSection.DescendantNodes().OfType<TypeDeclarationSyntax>()
            .Where(t => t.Type is NamedTypeSyntax)
            .GroupBy(t => (string)t.Identifier.Value!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Type.ToString().Trim(), StringComparer.OrdinalIgnoreCase);
        foreach (var pointer in windows.InterfaceSection.DescendantNodes().OfType<TypeDeclarationSyntax>()
                     .Where(t => t.Type is PointerTypeSyntax or ProceduralTypeSyntax))
        {
            aliases[(string)pointer.Identifier.Value!] = "Pointer";        // typed pointers and callbacks: one machine word
        }

        var declared = Externals(windows.InterfaceSection).GroupBy(e => e.Export, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(e => Shape(e.Routine, aliases)).ToList(), StringComparer.Ordinal);

        var privateImports = Externals(system.ImplementationSection).ToList();
        Assert.True(privateImports.Count >= 20, "System declares only " + privateImports.Count + " imports");
        var problems = new List<string>();
        foreach (var (export, routine) in privateImports)
        {
            var shape = Shape(routine, aliases);
            if (!declared.TryGetValue(export, out var shapes))
            {
                problems.Add($"{export}: not declared by Windows");
            }
            else if (!shapes.Contains(shape, StringComparer.Ordinal))
            {
                problems.Add($"{export}: System {shape} <> Windows {string.Join(" | ", shapes)}");
            }
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    private static IEnumerable<(string Export, RoutineDeclarationSyntax Routine)> Externals(SyntaxNode section) =>
        section.DescendantNodes().OfType<RoutineDeclarationSyntax>()
            .Select(r => (Routine: r, External: r.Directives.FirstOrDefault(d => d.Keyword.ContextualKind == SyntaxKind.ExternalKeyword)))
            .Where(x => x.External?.NameValue is LiteralExpressionSyntax { Token.Value: string })
            .Select(x => ((string)((LiteralExpressionSyntax)x.External!.NameValue!).Token.Value!, x.Routine));

    /// <summary><c>function(var T1, T2): R</c> with every type resolved through the Windows aliases to its base.</summary>
    private static string Shape(RoutineDeclarationSyntax routine, IReadOnlyDictionary<string, string> aliases)
    {
        string Resolve(string type)
        {
            for (var i = 0; i < 10 && aliases.TryGetValue(type, out var target); i++)
            {
                type = target.StartsWith("type ", StringComparison.OrdinalIgnoreCase) ? target[5..].Trim() : target;
            }

            return type.ToLowerInvariant() switch
            {
                "longword" or "dword" or "uint" or "ulong" => "cardinal",
                "longint" => "integer",
                "nativeuint" or "thandle" => "nativeuint",
                _ => type.ToLowerInvariant(),
            };
        }

        var heading = routine.Heading;
        var parameters = heading.ParameterList?.Parameters
            .SelectMany(p => p.Identifiers.Select(_ => (p.Modifier is { } m ? m.Text.ToLowerInvariant() + " " : "") + (p.Type is null ? "untyped" : Resolve(p.Type.ToString().Trim()))))
            ?? [];
        var result = heading.ResultType is null ? "" : ": " + Resolve(heading.ResultType.ToString().Trim());
        return heading.Keyword.Text.ToLowerInvariant() + "(" + string.Join(", ", parameters) + ")" + result;
    }

    [GeneratedRegex(@"array\s*\[[^\]]*\]\s*of\s+(Char|AnsiChar)\b", RegexOptions.IgnoreCase)]
    private static partial Regex NarrowCharBuffer();

    /// <summary>RTL-834: fixed-size character buffers of the bindings are <c>array[0..N-1] of WideChar</c>.</summary>
    [Fact]
    [Trait("Req", "RTL-834")]
    public void Bindings_DeclareCharacterBuffersAsWideChar()
    {
        var wide = 0;
        foreach (var path in Directory.GetFiles(WinApiDirectory, "*.pas"))
        {
            var code = Comment().Replace(File.ReadAllText(path), " ");
            var narrow = NarrowCharBuffer().Matches(code).Select(m => m.Value).ToList();
            Assert.True(narrow.Count == 0, Path.GetFileName(path) + ": " + string.Join(", ", narrow));
            wide += Regex.Matches(code, @"(?i)array\s*\[\s*0\s*\.\.[^\]]*\]\s*of\s+WideChar\b").Count;
        }

        Assert.True(wide > 0, "no WideChar buffer found");
    }

    /// <summary>RTL-814: the <c>.def</c> files list no data exports (no <c>DATA</c> keyword).</summary>
    [Fact]
    [Trait("Req", "RTL-814")]
    public void Def_ListsNoDataExports()
    {
        foreach (var path in Directory.GetFiles(DefFileTests.DefDirectory, "*.def"))
        {
            var data = File.ReadAllLines(path).Where(l => !l.TrimStart().StartsWith(';'))
                .Where(l => Regex.IsMatch(l, @"\bDATA\b")).ToList();
            Assert.True(data.Count == 0, Path.GetFileName(path) + ": " + string.Join(", ", data));
        }
    }

    /// <summary>RTL-813: import libraries are build artefacts; none is kept under <c>rtl/</c> (outside build output).</summary>
    [Fact]
    [Trait("Req", "RTL-813")]
    public void ImportLibraries_AreNotKeptInTheSourceTree()
    {
        var root = Path.Combine(RtlSetup.RepositoryRoot, "rtl");
        var libs = Directory.EnumerateFiles(root, "*.lib", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f))
            .Where(f => !f.Split(Path.DirectorySeparatorChar).Any(p => p is "bin" or "obj"))
            .ToList();
        Assert.True(libs.Count == 0, string.Join(", ", libs));
    }

    /// <summary>The security note of RTL-1231 with single spaces.</summary>
    public const string HashSecurityNote =
        "MD5 and SHA-1 are provided for compatibility with existing file formats and protocols only. "
        + "They are not suitable for security purposes - signatures, certificates, password storage, or protecting data against "
        + "deliberate modification - because collisions can be produced for both. Use THashSHA2 where security matters. "
        + "CRC-32, the Bob Jenkins hash and FNV-1a are checksums and hash-table functions and give no protection against "
        + "deliberate modification.";

    /// <summary>The phrase a comment directly in front of each of the two declarations must contain (RTL-1231).</summary>
    public const string HashSecurityPhrase = "not suitable for security purposes";

    /// <summary>
    /// What <paramref name="source"/> (the text of unit <c>Hash</c>) lacks of RTL-1231: the note in a comment of the
    /// interface part before the first type declaration, and the phrase in the comment directly in front of
    /// <c>THashMD5 = record</c> and of <c>THashSHA1 = record</c>. White space inside comments is free.
    /// </summary>
    public static List<string> HashSecurityNoteProblems(string source)
    {
        static string Normalise(string text) => Regex.Replace(text, @"\s+", " ");

        var problems = new List<string>();
        var comments = Comment().Matches(source).ToList();
        var code = Comment().Replace(source, m => new string(' ', m.Length));   // same offsets, no comment text

        var start = Regex.Match(code, @"\binterface\b", RegexOptions.IgnoreCase);
        var firstType = start.Success ? new Regex(@"\btype\b", RegexOptions.IgnoreCase).Match(code, start.Index) : Match.Empty;
        if (!firstType.Success
            || !comments.Any(c => c.Index > start.Index && c.Index + c.Length <= firstType.Index && Normalise(c.Value).Contains(HashSecurityNote, StringComparison.Ordinal)))
        {
            problems.Add("no comment between 'interface' and the first 'type' holds the sentences of RTL-1231");
        }

        foreach (var name in new[] { "THashMD5", "THashSHA1" })
        {
            var declaration = Regex.Match(code, $@"\b{name}\s*=\s*record\b", RegexOptions.IgnoreCase);
            if (!declaration.Success)
            {
                problems.Add($"no declaration '{name} = record'");
                continue;
            }

            // the comments that end directly in front of the declaration (only white space in between)
            var text = "";
            var at = declaration.Index;
            foreach (var c in comments.AsEnumerable().Reverse().Where(c => c.Index < declaration.Index))
            {
                if (c.Index + c.Length > at || !string.IsNullOrWhiteSpace(source[(c.Index + c.Length)..at]))
                {
                    break;
                }

                text = c.Value + " " + text;
                at = c.Index;
            }

            if (!Normalise(text).Contains(HashSecurityPhrase, StringComparison.Ordinal))
            {
                problems.Add($"'{name} = record' is not directly preceded by a comment with '{HashSecurityPhrase}'");
            }
        }

        return problems;
    }

    /// <summary>
    /// RTL-1231: the interface part of <c>Hash.pas</c> - the text Go to Declaration shows - states in a comment before
    /// its first type declaration that MD5 and SHA-1 are not for security purposes (the exact sentences), and the
    /// declarations of <c>THashMD5</c> and <c>THashSHA1</c> are each directly preceded by a comment that says so.
    /// The check itself is proved on texts that lack the note, have it in the wrong place, or lack one phrase.
    /// </summary>
    [Fact]
    [Trait("Req", "RTL-1231")]
    [Trait("Phase", "8")]
    public void Hash_StatesThatMd5AndSha1AreNotForSecurity()
    {
        var path = Path.Combine(SourceDirectory, "Hash.pas");
        Assert.True(File.Exists(path), path + " does not exist");
        var problems = HashSecurityNoteProblems(File.ReadAllText(path));
        Assert.True(problems.Count == 0, string.Join("\n", problems));

        // self-check
        var note = HashSecurityNote.Replace(". ", ".\n  ", StringComparison.Ordinal);
        string Unit(string beforeType, string beforeMd5, string beforeSha1, string afterType = "") =>
            $"unit Hash;\ninterface\nuses SysUtils;\n{beforeType}\ntype\n{afterType}  EHashException = class(Exception);\n"
            + $"{beforeMd5}\n  THashMD5 = record\n  end;\n{beforeSha1}\n  THashSHA1 = record\n  end;\nimplementation\nend.\n";
        const string phrase = "  { " + HashSecurityPhrase + " }";

        Assert.Empty(HashSecurityNoteProblems(Unit("{ " + note + " }", phrase, phrase)));
        Assert.Empty(HashSecurityNoteProblems(Unit("{ " + note + " }", "  // MD5:\n  // " + HashSecurityPhrase, phrase)));
        Assert.Single(HashSecurityNoteProblems(Unit("{ MD5 and SHA-1 are provided for compatibility. }", phrase, phrase)));
        Assert.Single(HashSecurityNoteProblems(Unit("", phrase, phrase, afterType: "{ " + note + " }\n")));
        Assert.Single(HashSecurityNoteProblems(Unit("{ " + note + " }", "  { RFC 1321 }", phrase)));
        Assert.Single(HashSecurityNoteProblems(Unit("{ " + note + " }", phrase, "  { FIPS 180-4 }")));
        Assert.Single(HashSecurityNoteProblems(Unit("{ " + note + " }", phrase + "\n  TOther = Integer;", phrase)));
        Assert.Equal(3, HashSecurityNoteProblems(Unit("", "", "")).Count);
        Assert.Equal(3, HashSecurityNoteProblems("unit Hash;\ninterface\nimplementation\nend.\n").Count);
    }

    /// <summary>
    /// What the body of <c>THash.DigestsEqual</c> in <paramref name="source"/> (the text of unit <c>Hash</c>) lacks of
    /// RTL-1233: exactly one loop, a counting <c>for</c> loop, and nothing in it whose execution depends on the bytes -
    /// no <c>if</c>, <c>case</c>, <c>while</c>, <c>repeat</c>, nested <c>for</c>, <c>try</c>, <c>raise</c>,
    /// <c>goto</c>, <c>Exit</c>, <c>Break</c> or <c>Continue</c>. Checked on the syntax tree, so comments and
    /// identifiers that merely contain these words do not count.
    /// </summary>
    public static List<string> DigestsEqualProblems(string source)
    {
        static bool IsLoop(SyntaxNode node) =>
            node is ForStatementSyntax or ForInStatementSyntax or WhileStatementSyntax or RepeatStatementSyntax;

        var problems = new List<string>();
        var tree = SyntaxTree.Parse(SourceText.From(Path.Combine(SourceDirectory, "Hash.pas"), source), null, PhysicalFileSystem.Instance);
        var body = tree.Root.DescendantNodes().OfType<RoutineDeclarationSyntax>()
            .FirstOrDefault(r => r.Block is not null
                && Regex.Replace(r.Heading.Name.ToString(), @"\s+", "").Equals("THash.DigestsEqual", StringComparison.OrdinalIgnoreCase))
            ?.Block;
        if (body is null)
        {
            problems.Add("no implementation of THash.DigestsEqual");
            return problems;
        }

        var loops = body.DescendantNodes().Where(IsLoop).ToList();
        if (loops.Count != 1)
        {
            problems.Add($"the body has {loops.Count} loops, expected exactly one");
        }

        if (loops.Count > 0 && loops[0] is not ForStatementSyntax)
        {
            problems.Add("the loop is not a counting 'for' loop");
        }

        foreach (var node in loops.Take(1).SelectMany(l => l.DescendantNodes()))
        {
            var found = node switch
            {
                IfStatementSyntax => "if",
                CaseStatementSyntax => "case",
                WhileStatementSyntax => "while",
                RepeatStatementSyntax => "repeat",
                ForStatementSyntax or ForInStatementSyntax => "for",
                TryFinallyStatementSyntax or TryExceptStatementSyntax => "try",
                RaiseStatementSyntax => "raise",
                GotoStatementSyntax => "goto",
                IdentifierNameSyntax { Identifier.Text: var name }
                    when name.Equals("Exit", StringComparison.OrdinalIgnoreCase)
                        || name.Equals("Break", StringComparison.OrdinalIgnoreCase)
                        || name.Equals("Continue", StringComparison.OrdinalIgnoreCase) => name,
                _ => null,
            };
            if (found is not null)
            {
                problems.Add($"the loop contains '{found}'");
            }
        }

        return problems;
    }

    /// <summary>
    /// RTL-1233: <c>THash.DigestsEqual</c> compares in constant time - its body in <c>Hash.pas</c> has exactly one loop,
    /// which runs over the whole length and contains no statement that leaves it or branches on the bytes. The check
    /// itself is proved on bodies that exit early, branch, loop twice or use another kind of loop.
    /// </summary>
    [Fact]
    [Trait("Req", "RTL-1233")]
    [Trait("Phase", "8")]
    public void Hash_DigestsEqualHasNoEarlyExit()
    {
        var path = Path.Combine(SourceDirectory, "Hash.pas");
        Assert.True(File.Exists(path), path + " does not exist");
        var problems = DigestsEqualProblems(File.ReadAllText(path));
        Assert.True(problems.Count == 0, string.Join("\n", problems));

        // self-check
        static string Unit(string body, string name = "THash.DigestsEqual") =>
            "unit Hash;\ninterface\nuses SysUtils;\ntype\n  THash = record\n"
            + "    class function DigestsEqual(const ADigest1, ADigest2: TBytes): Boolean; static;\n  end;\nimplementation\n"
            + $"class function {name}(const ADigest1, ADigest2: TBytes): Boolean;\nvar\n  I: Integer;\n  Diff: Cardinal;\n"
            + $"begin\n{body}\nend;\nend.\n";
        const string lengths = "  Result := False;\n  if Length(ADigest1) <> Length(ADigest2) then\n    Exit;\n  Diff := 0;\n";
        const string loop = "  for I := 0 to Length(ADigest1) - 1 do\n";
        const string step = "Diff := Diff or (Cardinal(ADigest1[I]) xor Cardinal(ADigest2[I]));\n";
        const string result = "  Result := Diff = 0;";

        // the length test before the loop is allowed; the words in a comment or inside another identifier do not count
        Assert.Empty(DigestsEqualProblems(Unit(lengths + loop + "    " + step + result)));
        Assert.Empty(DigestsEqualProblems(Unit(lengths + loop + "  begin\n    { no if, no Exit, no Break }\n    " + step + "  end;\n" + result)));
        Assert.Empty(DigestsEqualProblems(Unit(lengths + loop + "    " + step.Replace("Diff", "ExitCode") + result.Replace("Diff", "ExitCode"))));

        string Only(string body) => Assert.Single(DigestsEqualProblems(Unit(body)));
        // the early exit this rule is about: reported for its 'if' and for its 'Exit'
        var early = DigestsEqualProblems(Unit(lengths + loop + "    if ADigest1[I] <> ADigest2[I] then\n      Exit;\n  Result := True;"));
        Assert.Equal(2, early.Count);
        Assert.Contains("the loop contains 'if'", early);
        Assert.Contains("the loop contains 'Exit'", early);
        Assert.Contains("'Break'", Only(lengths + loop + "  begin\n    " + step + "    Break;\n  end;\n" + result));
        Assert.Contains("'continue'", Only(lengths + loop + "  begin\n    continue;\n    " + step + "  end;\n" + result));
        Assert.Contains("'if'", Only(lengths + loop + "    if ADigest1[I] <> ADigest2[I] then\n      Diff := 1;\n" + result));
        Assert.Contains("'case'", Only(lengths + loop + "    case ADigest1[I] xor ADigest2[I] of\n      0: ;\n    else\n      Diff := 1;\n    end;\n" + result));
        Assert.Contains("'raise'", Only(lengths + loop + "  begin\n    " + step + "    raise Exception.Create('x');\n  end;\n" + result));
        Assert.Contains("'goto'", Only(lengths + loop + "  begin\n    " + step + "    goto Away;\n  end;\n" + result));
        Assert.Contains("2 loops", Only(lengths + loop + "    " + step + loop + "    " + step + result));
        Assert.Contains("0 loops", Only(lengths + result));
        Assert.Contains("not a counting", Only(lengths + "  I := 0;\n  while I < Length(ADigest1) do\n  begin\n    " + step + "    I := I + 1;\n  end;\n" + result));
        Assert.Contains("not a counting", Only(lengths + "  I := 0;\n  repeat\n    " + step + "    I := I + 1;\n  until I >= Length(ADigest1);\n" + result));
        // a nested loop: two loops, and the outer one contains a 'for'
        Assert.Equal(2, DigestsEqualProblems(Unit(lengths + loop + loop + "    " + step + result)).Count);
        Assert.Contains("no implementation", Assert.Single(DigestsEqualProblems(Unit(lengths + loop + "    " + step + result, "THash.DigestAsString"))));
    }
}
