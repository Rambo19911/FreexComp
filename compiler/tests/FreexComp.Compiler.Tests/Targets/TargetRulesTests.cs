using System.Text;

namespace FreexComp.Compiler.Tests.Targets;

/// <summary>
/// targets.md CMP-1031 (task C-284), permanent: no source file of <c>compiler/src</c> outside <c>FreexComp.Compiler/Targets/</c>
/// states a target fact as a literal (CMP-1000). The list grows with every target that is added.
/// </summary>
[Trait("Phase", "8")]
[Trait("Kind", "Unit")]
public sealed class TargetRulesTests
{
    private static readonly string[] Forbidden =
    [
        "x86_64-pc-windows-msvc", "i386:x86-64", "/machine:x64", "machine:x64", "__C_specific_handler", "__chkstk", "ntdll.dll",
        "\"WIN64\"", "\"CPUX64\"", "\"CPU64BITS\"",
    ];

    [Fact]
    [Trait("Req", "CMP-1031")]
    public void NoHardCodedTargetFactsOutsideTargets()
    {
        var root = Path.Combine(RepoPaths.Root, "compiler", "src");
        var targets = Path.Combine(root, "FreexComp.Compiler", "Targets") + Path.DirectorySeparatorChar;
        var files = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.StartsWith(targets, StringComparison.OrdinalIgnoreCase))
            .Where(f => !Path.GetRelativePath(root, f).Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .Order(StringComparer.Ordinal).ToArray();
        Assert.True(files.Length > 100, $"only {files.Length} source files found under {root}");
        Assert.True(Directory.EnumerateFiles(targets, "*.cs").Any(), "the Targets folder is missing");

        var findings = new List<string>();
        foreach (var file in files)
        {
            var lines = WithoutComments(File.ReadAllText(file)).Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (var literal in Forbidden.Where(l => lines[i].Contains(l, StringComparison.Ordinal)))
                {
                    findings.Add($"{Path.GetRelativePath(root, file)}({i + 1}): {literal}");
                }
            }
        }

        Assert.True(findings.Count == 0, "target facts outside FreexComp.Compiler/Targets (read them from the TargetInfo of the compilation):\n" + string.Join("\n", findings));

        // the scanner itself: comments go, code and string literals stay
        Assert.Equal("a = \"x // y\"; \nb = '/'; \n\nc\n",WithoutComments("a = \"x // y\"; // __chkstk\nb = '/'; /* ntdll.dll\n*/\nc\n/// <c>\"WIN64\"</c>"));
        Assert.Contains("ntdll.dll", WithoutComments("var s = @\"C:\\\"; var t = \"ntdll.dll\"; // gone"), StringComparison.Ordinal);
    }

    /// <summary>
    /// <paramref name="text"/> without <c>//</c> and <c>/* */</c> comments (line structure kept); string and character literals —
    /// regular, verbatim and raw — are copied unchanged.
    /// </summary>
    private static string WithoutComments(string text)
    {
        var result = new StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\n')
                {
                    i++;
                }
            }
            else if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                var end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                var stop = end < 0 ? text.Length : end + 2;
                result.Append('\n', text[i..stop].Count(ch => ch == '\n'));
                i = stop;
            }
            else if (c == '"' && text.AsSpan(i).StartsWith("\"\"\""))
            {
                var quotes = 0;
                while (i + quotes < text.Length && text[i + quotes] == '"')
                {
                    quotes++;
                }

                var end = text.IndexOf(new string('"', quotes), i + quotes, StringComparison.Ordinal);
                var stop = end < 0 ? text.Length : end + quotes;
                result.Append(text, i, stop - i);
                i = stop;
            }
            else if (c == '"' || c == '\'')
            {
                var verbatim = c == '"' && i > 0 && (text[i - 1] == '@' || (text[i - 1] == '$' && i > 1 && text[i - 2] == '@'));
                var start = i++;
                while (i < text.Length && text[i] != '\n')
                {
                    if (verbatim && text[i] == '"' && i + 1 < text.Length && text[i + 1] == '"')
                    {
                        i += 2;
                    }
                    else if (!verbatim && text[i] == '\\')
                    {
                        i += 2;
                    }
                    else if (text[i++] == c)
                    {
                        break;
                    }
                }

                i = Math.Min(i, text.Length);
                result.Append(text, start, i - start);
            }
            else
            {
                result.Append(c);
                i++;
            }
        }

        return result.ToString();
    }
}
