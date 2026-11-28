using System.Text.RegularExpressions;
using FreexComp.Compiler;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;

namespace FreexComp.Rtl.Tests.Harness;

/// <summary>
/// RTL rules whose reference list lives in a specification document (<c>docs/</c>, git-ignored working material):
/// the helper routines of <c>docs/interfaces/rtl-abi.md</c> (RTL-140) and the message constants the UI framework
/// specification uses (RTL-850). Like <see cref="RequirementCoverageTests.Req_Coverage"/> they need the documents.
/// </summary>
[Trait("Phase", "2")]
[Trait("Category", "Spec")]
public sealed partial class SpecConformanceTests
{
    public static string AbiPath => Path.Combine(RtlSetup.RepositoryRoot, "docs", "interfaces", "rtl-abi.md");

    public static string UiFrameworkSpecDirectory => Path.Combine(RtlSetup.RepositoryRoot, "docs", "spec", "ui-framework");

    [GeneratedRegex(@"^##\s+(\d+)\.")]
    private static partial Regex Chapter();

    [GeneratedRegex(@"^\s*(procedure|function)\s+(\w+)")]
    private static partial Regex Declaration();

    [GeneratedRegex(@"\{[^}]*\}|\(\*.*?\*\)|//[^\n]*", RegexOptions.Singleline)]
    private static partial Regex Comment();

    /// <summary>
    /// RTL-140: every helper routine of rtl-abi.md section 9 and every resource routine of section 11 is declared in the
    /// interface of <c>System.pas</c> with exactly the name and signature given there; there is no <c>_Rtl</c> prefix.
    /// </summary>
    [Fact]
    [Trait("Req", "RTL-140")]
    public void System_DeclaresEveryAbiHelperRoutine()
    {
        if (!File.Exists(AbiPath))
        {
            Assert.Skip(AbiPath + " is not present (plans are not part of the repository)");
        }

        var helpers = AbiDeclarations(File.ReadAllLines(AbiPath), [9, 11]);
        Assert.True(helpers.Count > 100, $"only {helpers.Count} declarations found in rtl-abi.md sections 9 and 11");

        var system = File.ReadAllText(Path.Combine(RtlSetup.RepositoryRoot, "rtl", "src", "System.pas"));
        var implementation = Regex.Match(system, @"(?m)^implementation\b");
        Assert.True(implementation.Success, "System.pas has no implementation section");
        var interfacePart = Normalize(Comment().Replace(system[..implementation.Index], " "));
        var missing = helpers.Where(h => !interfacePart.Contains(Normalize(h), StringComparison.Ordinal)).ToList();
        Assert.True(missing.Count == 0, "not declared in the interface of System.pas as in rtl-abi.md:\n" + string.Join("\n", missing));
        Assert.DoesNotMatch(@"\b_Rtl\w*", Comment().Replace(system, " "));
    }

    /// <summary>The <c>procedure</c>/<c>function</c> lines (comment removed) of the Pascal code blocks of the given chapters.</summary>
    public static List<string> AbiDeclarations(IEnumerable<string> lines, int[] chapters)
    {
        var result = new List<string>();
        var chapter = 0;
        var inCode = false;
        foreach (var line in lines)
        {
            if (Chapter().Match(line) is { Success: true } heading)
            {
                chapter = int.Parse(heading.Groups[1].Value);
            }

            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                inCode = !inCode && line.StartsWith("```pascal", StringComparison.Ordinal);
                continue;
            }

            if (inCode && chapters.Contains(chapter) && Declaration().IsMatch(line))
            {
                var code = line.Split("//")[0].Trim();
                result.Add(code);
            }
        }

        return result;
    }

    private static string Normalize(string text) => Regex.Replace(text, @"\s+", " ");

    [Fact]
    public void AbiDeclarations_ReadsOnlyTheCodeOfTheChosenChapters()
    {
        string[] doc =
        [
            "## 8. Other", "```pascal", "procedure _No;", "```",
            "## 9. Helpers", "procedure _NotCode;", "```pascal", "procedure _A(X: Integer);   // comment", "  function  _B: Pointer;", "```",
            "```text", "procedure _NotPascal;", "```",
            "## 10. Next", "```pascal", "procedure _Late;", "```",
        ];
        Assert.Equal(["procedure _A(X: Integer);", "function  _B: Pointer;"], AbiDeclarations(doc, [9]));
    }

    /// <summary>
    /// RTL-1265 (b), card F10-030 (the member list equals the declared members, parsed with the compiler): every
    /// <c>pascal</c> block of <c>docs/spec/rtl/helpers.md</c> against the helpers of the RTL units — the helped type,
    /// and the public members as normalised declarations. Members of a task listed on the <c>Landed tasks:</c> line must
    /// be declared; members of a pending task may be absent, but a name that is declared must be declared as listed; a
    /// public member that is not listed and a helper without a block are errors. Self-check: one changed parameter type,
    /// one missing member and one extra member give exactly three differences.
    /// </summary>
    [Fact]
    [Trait("Req", "RTL-1265")]
    [Trait("Phase", "8")]
    [Trait("Category", "Integration")]
    public void HelperMembersMatchSpec()
    {
        if (!File.Exists(HelperMembers.DocumentPath))
        {
            Assert.Skip(HelperMembers.DocumentPath + " is not present (plans are not part of the repository)");
        }

        var markdown = File.ReadAllText(HelperMembers.DocumentPath);
        var document = HelperMembers.FromDocument(markdown);
        Assert.True(document.Count == 19, $"the member list holds {document.Count} helper blocks, RTL-1265 (a) names nineteen");
        Assert.All(document, h => Assert.All(h.Members, m => Assert.True(m.Task is not null, $"{h.Name}: '{m.Declaration}' has no '// task:' line")));
        var declared = RtlUnitRulesTests.DeclaredHelpers(RtlUnitRulesTests.Units()).Select(d => d.Helper).ToList();
        var problems = HelperMembers.Compare(document, HelperMembers.LandedTasks(markdown), declared);
        Assert.True(problems.Count == 0, string.Join("\n", problems));

        // self-check
        const string Doc = "Landed tasks: R-1\n\n```pascal\nTIntegerHelper = record helper for Integer\npublic\n  // task: R-1\n"
            + "  function Twice(X: Integer): Integer;\n  function Half: Integer; inline;     // = Classic\n  const MaxValue = High(Integer);\n"
            + "  // task: R-2\n  function Later: Integer;\nend;\n```\n";
        const string Unit = "unit P;\ninterface\ntype\n  TIntegerHelper = record helper for Integer\n  private\n    function Hidden: Integer;\n"
            + "  public\n    const MaxValue = High(Integer);\n    function TWICE(X: Int64): Integer;\n    function Extra: Integer;\n  end;\n"
            + "implementation\nend.\n";
        var probeDoc = HelperMembers.FromDocument(Doc);
        Assert.Equal("Classic", Assert.Single(probeDoc).Members.Single(m => m.Name == "Half").Wraps);
        Assert.Equal("R-2", probeDoc[0].Members.Single(m => m.Name == "Later").Task);
        var tree = SyntaxTree.Parse(SourceText.From(Path.Combine(RtlUnitRulesTests.SourceDirectory, "P.pas"), Unit), null, PhysicalFileSystem.Instance);
        var probe = HelperMembers.Declarations(Assert.IsType<UnitSyntax>(tree.Root), "P").ToList();
        var differences = HelperMembers.Compare(probeDoc, HelperMembers.LandedTasks(Doc), probe);
        Assert.True(differences.Count == 3, string.Join("\n", differences));
    }

    [GeneratedRegex(@"\b(?:WM|EM|EN|BM|BN|BST|LB|LBN|CB|CBN|STM|SBM|TCM|LVM|TVM|TB|SB|PBM|TBM)_[A-Z0-9_]+\b(?!\*)")]
    private static partial Regex MessageName();

    /// <summary>
    /// RTL-850: every window and control message constant that the ui-framework specification of Phases 4-7 uses is
    /// declared by <c>Messages</c> (window and control messages), <c>CommCtrl</c> (common-control messages) or <c>RichEdit</c> (rich edit messages), or by
    /// <c>Windows</c> for the codes it owns. Names ending in <c>*</c> stand for a family; the Phase 8+ document is skipped.
    /// </summary>
    [Fact]
    [Trait("Req", "RTL-850")]
    public void Messages_DeclaresTheMessagesTheUiFrameworkUses()
    {
        if (!Directory.Exists(UiFrameworkSpecDirectory))
        {
            Assert.Skip(UiFrameworkSpecDirectory + " is not present (plans are not part of the repository)");
        }

        var used = Directory.GetFiles(UiFrameworkSpecDirectory, "*.md")
            .Where(f => !Path.GetFileName(f).StartsWith("phase8", StringComparison.OrdinalIgnoreCase))
            .SelectMany(f => MessageName().Matches(File.ReadAllText(f)).Select(m => m.Value))
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains("WM_PAINT", used);

        var winapi = Path.Combine(RtlSetup.RepositoryRoot, "rtl", "winapi", "src");
        var declared = string.Concat(new[] { "Messages.pas", "CommCtrl.pas", "Windows.pas", "RichEdit.pas" }
            .Select(f => Comment().Replace(File.ReadAllText(Path.Combine(winapi, f)), " ")));
        var missing = used.Where(n => !Regex.IsMatch(declared, @"\b" + n + @"\s*=")).Order(StringComparer.Ordinal).ToList();
        Assert.True(missing.Count == 0, "message constants used by the ui-framework spec but not declared: " + string.Join(", ", missing));
    }
}
