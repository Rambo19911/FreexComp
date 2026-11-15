using System.Collections.Immutable;
using FreexComp.Compiler.Text;

namespace FreexComp.Compiler.Syntax;

/// <summary>
/// The preprocessed token stream (CMP-111): the main file's tokens with the tokens of <c>{$I file}</c> includes
/// inserted at the directive (LANG-875). Each token records its file (<see cref="SyntaxToken.File"/>) and has spans
/// relative to it; diagnostics inside an include report the include file's own path, line and column. Defines and
/// switches carry across include boundaries; conditionals must balance within each file (LANG-863).
/// The include files' end-of-file tokens are not part of the stream (their trailing trivia stays with those files).
/// Without a file system nothing is expanded, exactly like <see cref="Lexer.Tokenize"/>.
/// </summary>
internal sealed class Preprocessor
{
    /// <summary>LANG-875: nesting depth limit for include files.</summary>
    public const int MaxIncludeDepth = 16;

    private readonly PreprocessorContext _context;
    private readonly IFileSystem? _fileSystem;
    private readonly List<PascalLexer> _lexers = [];
    private readonly List<IncludedFile?> _entries = [];     // parallel to _lexers; null for the main file
    private bool _started;

    public Preprocessor(SourceText main, ParseOptions? options, IFileSystem? fileSystem)
    {
        _context = new PreprocessorContext(options);
        _fileSystem = fileSystem;
        _lexers.Add(new PascalLexer(main, _context, expandIncludes: fileSystem is not null));
        _entries.Add(null);
    }

    /// <summary>Every include occurrence in the order opened, with all its own tokens incl. its end-of-file token.</summary>
    public List<IncludedFile> Includes { get; } = [];

    /// <summary>Lexical and directive diagnostics of the main file and every include, in the order reported.</summary>
    public ImmutableArray<Diagnostic> Diagnostics => [.. _context.Diagnostics];

    public SyntaxToken Lex()
    {
        while (true)
        {
            var lexer = _lexers[^1];
            var token = lexer.Lex();
            if (token.Kind == SyntaxKind.None)
            {
                Open(lexer, lexer.TakePendingInclude()!);
                continue;
            }

            _entries[^1]?.Tokens.Add(token);
            if (!_started)
            {
                _started = true;
                _context.IsUnitFile = token.Kind == SyntaxKind.UnitKeyword;     // C-127 (W1003); directives before the heading are not checked
            }

            if (token.Kind == SyntaxKind.EndOfFileToken && _lexers.Count > 1)
            {
                _lexers.RemoveAt(_lexers.Count - 1);
                _entries.RemoveAt(_entries.Count - 1);
                continue;
            }

            return token;
        }
    }

    private void Open(PascalLexer includer, DirectiveTriviaInfo directive)
    {
        var name = DirectiveProcessor.IncludeFileName(directive);
        if (_lexers.Count > MaxIncludeDepth)
        {
            includer.ReportAt("E1204", directive.Span);
            return;
        }

        if (Resolve(includer.Source.Path, name) is not { } path)
        {
            includer.ReportAt("E1202", directive.Span, name);
            return;
        }

        string text;
        try
        {
            text = _fileSystem!.ReadAllText(path);
        }
        catch (IOException e)
        {
            includer.ReportAt("E0003", directive.Span, path, e.Message);
            return;
        }

        var source = SourceText.From(path, text);
        var entry = new IncludedFile(source, []);
        Includes.Add(entry);
        _lexers.Add(new PascalLexer(source, _context, expandIncludes: true));
        _entries.Add(entry);
    }

    /// <summary>
    /// LANG-875: the directory of the including file, then <see cref="ParseOptions.SearchPaths"/> in order; default
    /// extension <c>.inc</c> when the name has none. An absolute name is used as is.
    /// </summary>
    private string? Resolve(string includerPath, string name)
    {
        var file = Path.HasExtension(name.Replace('\\', '/').Split('/')[^1]) ? name : name + ".inc";
        if (IsAbsolute(file))
        {
            var absolute = PathUtil.Normalize(file);
            return _fileSystem!.FileExists(absolute) ? absolute : null;
        }

        var directories = new List<string> { includerPath[..includerPath.LastIndexOf('\\')] };
        directories.AddRange(_context.Options?.SearchPaths ?? []);
        foreach (var directory in directories)
        {
            if (!IsAbsolute(directory))
            {
                continue;
            }

            var candidate = PathUtil.Normalize(directory + @"\" + file);
            if (_fileSystem!.FileExists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool IsAbsolute(string path)
    {
        var p = path.Replace('/', '\\');
        return p.StartsWith(@"\\", StringComparison.Ordinal) || (p.Length >= 3 && char.IsAsciiLetter(p[0]) && p[1] == ':' && p[2] == '\\');
    }
}

/// <summary>One opened include file and the tokens read from it (spans relative to <see cref="Text"/>).</summary>
internal sealed record IncludedFile(SourceText Text, List<SyntaxToken> Tokens);
