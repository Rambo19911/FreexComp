using System.Text.RegularExpressions;

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

    [GeneratedRegex(@"\b(?:WM|EM|EN|BM|BN|BST|LB|LBN|CB|CBN|STM|SBM|TCM|LVM|TVM|TB|SB|PBM|TBM)_[A-Z0-9_]+\b(?!\*)")]
    private static partial Regex MessageName();

    /// <summary>
    /// RTL-850: every window and control message constant that the ui-framework specification of Phases 4-7 uses is
    /// declared by <c>Messages</c> (window and control messages) or <c>CommCtrl</c> (common-control messages), or by
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
        var declared = string.Concat(new[] { "Messages.pas", "CommCtrl.pas", "Windows.pas" }
            .Select(f => Comment().Replace(File.ReadAllText(Path.Combine(winapi, f)), " ")));
        var missing = used.Where(n => !Regex.IsMatch(declared, @"\b" + n + @"\s*=")).Order(StringComparer.Ordinal).ToList();
        Assert.True(missing.Count == 0, "message constants used by the ui-framework spec but not declared: " + string.Join(", ", missing));
    }
}
