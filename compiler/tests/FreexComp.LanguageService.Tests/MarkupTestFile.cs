using FreexComp.Compiler.Text;

namespace FreexComp.LanguageService.Tests;

/// <summary>language-service.md §12 markup: <c>$$</c> caret, <c>[|…|]</c> expected spans (in text order; not nested).</summary>
internal sealed record MarkupTestFile(string Text, int Caret, IReadOnlyList<TextSpan> Spans)
{
    public static MarkupTestFile Parse(string markup)
    {
        markup = markup.ReplaceLineEndings("\n");
        var text = new System.Text.StringBuilder();
        var spans = new List<TextSpan>();
        var caret = -1;
        var open = -1;
        for (var i = 0; i < markup.Length; i++)
        {
            if (string.CompareOrdinal(markup, i, "$$", 0, 2) == 0)
            {
                caret = text.Length;
                i++;
            }
            else if (string.CompareOrdinal(markup, i, "[|", 0, 2) == 0)
            {
                open = text.Length;
                i++;
            }
            else if (string.CompareOrdinal(markup, i, "|]", 0, 2) == 0 && open >= 0)
            {
                spans.Add(TextSpan.FromBounds(open, text.Length));
                open = -1;
                i++;
            }
            else
            {
                text.Append(markup[i]);
            }
        }

        return new MarkupTestFile(text.ToString(), caret, spans);
    }

    /// <summary>A project workspace (<c>P.pas</c> main) with the given files; returns the document of <paramref name="path"/>.</summary>
    public static Document Open(string path, params (string Name, string Text)[] files)
    {
        var workspace = new Workspace(TestFiles.Files(files));
        workspace.SetProject(TestFiles.Options());
        return workspace.GetDocument(TestFiles.PathOf(path));
    }
}
