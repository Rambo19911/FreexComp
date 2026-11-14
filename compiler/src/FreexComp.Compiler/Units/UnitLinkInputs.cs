using System.Collections.Immutable;
using FreexComp.Compiler.Emit;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;

namespace FreexComp.Compiler.Units;

/// <summary>
/// The <c>.pcu</c> <c>linkInputs</c> of one unit (architecture.md §6, C-077): the DLLs named by its <c>external '&lt;dll&gt;'</c>
/// declarations (normalised, sorted, CMP-430) and the files named by its <c>{$L file}</c> / <c>{$LINK file}</c> directives
/// (directives.md; absolute paths, directive order, each once): <c>.def</c> files go to <see cref="Defs"/> (CMP-620's first
/// lookup step), everything else to <see cref="Objects"/> (CMP-625's extra objects).
/// </summary>
internal sealed record UnitLinkInputs(ImmutableArray<string> Dlls, ImmutableArray<string> Objects, ImmutableArray<string> Defs)
{
    public static readonly UnitLinkInputs Empty = new([], [], []);

    /// <summary>
    /// Gathers the link inputs of a bound unit. A <c>{$L}</c> name is unquoted, gets <c>.obj</c> when it has no extension and is
    /// resolved against the directory of the file holding the directive (an include file's own directory); a missing file is
    /// E0002 at the directive.
    /// </summary>
    public static UnitLinkInputs Collect(UnitSymbol unit, SyntaxTree tree, IFileSystem fs, List<Diagnostic> diagnostics)
    {
        ImmutableArray<string> dlls = [.. LinkInputs.Imports([unit]).Select(i => i.DllName).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        var objects = new List<string>();
        var defs = new List<string>();
        foreach (var (file, directive) in LinkDirectives(tree))
        {
            var name = DirectiveProcessor.Unquote(directive.Argument).Replace('/', '\\');
            if (!Path.HasExtension(name))
            {
                name += ".obj";
            }

            var path = PathUtil.Normalize(Path.IsPathFullyQualified(name) ? name : Path.Combine(Path.GetDirectoryName(file.Path)!, name));
            if (!fs.FileExists(path))
            {
                var span = directive.Span;
                diagnostics.Add(Diagnostic.Create(
                    "E0002",
                    new Location(file.Path, span, new LinePositionSpan(file.Lines.GetLinePosition(span.Start), file.Lines.GetLinePosition(span.End))),
                    path));
                continue;
            }

            var list = string.Equals(Path.GetExtension(path), ".def", StringComparison.OrdinalIgnoreCase) ? defs : objects;
            if (!list.Contains(path, PathUtil.Comparer))
            {
                list.Add(path);
            }
        }

        return new UnitLinkInputs(dlls, [.. objects], [.. defs]);
    }

    /// <summary><c>{$L file}</c> (argument not starting with <c>+</c>/<c>-</c>) and <c>{$LINK file}</c> of the file and its includes, in order.</summary>
    internal static IEnumerable<(SourceText File, DirectiveTriviaInfo Directive)> LinkDirectives(SyntaxTree tree) =>
        tree.IncludedTrees.Prepend(tree)
            .SelectMany(t => t.Directives.Select(d => (t.Text, d)))
            .Where(p => p.d.Argument.Length > 0
                        && ((p.d.Name == "L" && p.d.Argument[0] is not ('+' or '-')) || p.d.Name == "LINK"));
}
