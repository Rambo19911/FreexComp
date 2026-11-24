using FreexComp.Compiler;
using FreexComp.Compiler.Syntax;

namespace FreexComp.Policy.Tests;

/// <summary>
/// The library source of one home directory: the directories the compiler compiles the runtime library, the UI framework
/// and the data-access library from. The home is the repository root, or the directory named by the environment variable
/// <c>FREEXCOMP_LAYOUT_HOME</c> (an installed tree), so the same checks run against both.
/// </summary>
internal sealed class LibrarySourceLayout
{
    public const string HomeVariable = "FREEXCOMP_LAYOUT_HOME";

    /// <summary>The alias table of the runtime library root.</summary>
    public const string AliasFile = "unit-aliases.json";

    /// <summary>
    /// The source directories relative to the home, '/' separated: the three library roots, then the Win32 binding units,
    /// which the compiler searches together with the runtime library root (<c>&lt;rtl root&gt;/../winapi/src</c>).
    /// </summary>
    public static readonly IReadOnlyList<string> RootNames = ["rtl/src", "ui-framework/src", "data/src", "rtl/winapi/src"];

    public LibrarySourceLayout(string home)
    {
        Home = Path.GetFullPath(home);
        Roots = [.. RootNames.Select(name => Path.Combine([Home, .. name.Split('/')]))];
    }

    /// <summary>The layout of <c>FREEXCOMP_LAYOUT_HOME</c> when that variable is set and not empty, else of the repository root.</summary>
    public static LibrarySourceLayout Current()
    {
        var home = Environment.GetEnvironmentVariable(HomeVariable);
        return new LibrarySourceLayout(string.IsNullOrWhiteSpace(home) ? RepoPaths.Root : home);
    }

    public string Home { get; }

    /// <summary>The absolute source directories, in the order of <see cref="RootNames"/>.</summary>
    public IReadOnlyList<string> Roots { get; }

    public string RtlRoot => Roots[0];

    public string FrameworkRoot => Roots[1];

    public string DataRoot => Roots[2];

    /// <summary>The path of <paramref name="path"/> relative to the home, '/' separated (for messages).</summary>
    public string Display(string path) => Path.GetRelativePath(Home, path).Replace('\\', '/');

    /// <summary>Every file under the source directories, sub-directories included, in ordinal path order.</summary>
    public IReadOnlyList<string> AllFiles() =>
        [.. Roots.Where(Directory.Exists).SelectMany(root => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)).Order(StringComparer.Ordinal)];

    /// <summary>The <c>.pas</c> files directly inside the source directories, in ordinal path order.</summary>
    public IReadOnlyList<string> UnitFiles() =>
        [.. Roots.Where(Directory.Exists).SelectMany(root => Directory.EnumerateFiles(root, "*.pas", SearchOption.TopDirectoryOnly))
            .Where(file => Path.GetExtension(file).Equals(".pas", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal)];

    /// <summary>True when <paramref name="path"/> lies in one of the source directories (or below one).</summary>
    public bool IsUnderRoot(string path)
    {
        var full = Path.GetFullPath(path);
        return Roots.Any(root => full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The unit name a source file declares: the text between its first tokens <c>unit</c> and <c>;</c> as the compiler's
    /// lexer reads them (comments and directives before the heading are trivia). Null when the file does not start with a
    /// unit heading.
    /// </summary>
    public static string? DeclaredUnitName(string file)
    {
        var tokens = Lexer.Tokenize(SourceText.From(file, File.ReadAllText(file)));
        if (tokens.Length < 3 || tokens[0].Kind != SyntaxKind.UnitKeyword)
        {
            return null;
        }

        var name = new System.Text.StringBuilder();
        for (var i = 1; i < tokens.Length && tokens[i].Kind != SyntaxKind.SemicolonToken; i++)
        {
            var expectDot = i % 2 == 0;
            if (expectDot != (tokens[i].Kind == SyntaxKind.DotToken) || tokens[i].IsMissing)
            {
                return null;
            }

            name.Append(tokens[i].Text);
        }

        return name.Length == 0 || name[^1] == '.' ? null : name.ToString();
    }
}
