using System.Text;
using FreexComp.Designer.Catalog;
using FreexComp.Designer.Model;
using FreexComp.Designer.Text;

namespace FreexComp.Designer.Tests.Text;

/// <summary>Round trip of the form corpora (form-format.md §2.8; testing.md §2).</summary>
public sealed class FormRoundTripTests
{
    private static readonly ComponentCatalog Catalog = TestCatalog.Instance;

    public static TheoryData<string> CanonicalFiles() => [.. FormParserTests.CorpusFiles("canonical")];

    public static TheoryData<string> NonCanonicalFiles() => [.. FormParserTests.CorpusFiles("noncanonical")];

    public static TheoryData<string> SemanticFiles() => [.. FormParserTests.CorpusFiles("semantic")];

    [Theory]
    [MemberData(nameof(CanonicalFiles))]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-FD-049")]
    [Trait("Req", "DSG-FD-040")]
    [Trait("Req", "DSG-FD-116")]
    public void CanonicalFilesRoundTripByteIdentical(string file)
    {
        var bytes = File.ReadAllBytes(TestPaths.Combine("forms/canonical/" + file));
        var text = new UTF8Encoding(false, true).GetString(bytes);
        Assert.DoesNotContain('\r', text);
        Assert.False(bytes is [0xEF, 0xBB, 0xBF, ..]);

        // D-118: the frame files of the corpus are frame documents, and the forms resolve their instances against them.
        // D-133: the levels of the inheritance chain are read against their ancestors — a resolved document, not the
        // read-only text of an unknown ancestor (F0107).
        var inherited = file.StartsWith("inherit-", StringComparison.Ordinal);
        var document = inherited
            ? InheritedFormRoundTripTests.ParseChainLevel(file)
            : FrameFormRoundTripTests.IsFrameFile(file)
                ? Support.FrameFixtures.ParseFrame(text, FrameFormRoundTripTests.CorpusFrames(), file)
                : Support.FrameFixtures.ParseForm(text, FrameFormRoundTripTests.CorpusFrames(), file);
        if (inherited)
        {
            Assert.Empty(document.Diagnostics);
            Assert.False(document.Root.IsPreserved);
            Assert.Equal(file != "inherit-base.form", document.Root.IsInheritedRoot);
        }

        var written = FormWriter.Write(document);

        Assert.Equal(text, written);
        Assert.Equal(bytes, Encoding.UTF8.GetBytes(written));
    }

    [Theory]
    [MemberData(nameof(NonCanonicalFiles))]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-FD-050")]
    public void NonCanonicalNormalisesAndIsIdempotent(string file)
    {
        var path = TestPaths.Combine("forms/noncanonical/" + file);
        var input = File.ReadAllText(path);

        var written = FormWriter.Write(Parse(input, file));

        Assert.NotEqual(input, written);
        Golden.Check(Path.ChangeExtension(path, ".expected.form"), written);
        Assert.Equal(written, FormWriter.Write(Parse(written, file))); // writing the canonical form again is a no-op
    }

    [Theory]
    [MemberData(nameof(SemanticFiles))]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-FD-026")]
    public void SemanticProblemFilesRoundTrip(string file)
    {
        var path = TestPaths.Combine("forms/semantic/" + file);
        var text = File.ReadAllText(path);
        var result = FormParser.Parse(text, file, Catalog);
        Assert.False(result.HasSyntaxErrors, string.Join("; ", result.Diagnostics));
        Assert.NotEmpty(result.Diagnostics);
        Golden.Check(Path.ChangeExtension(path, ".diag.txt"), string.Concat(result.Diagnostics.Select(d => d + "\n")));

        // Preserved objects and properties come back unchanged.
        Assert.Equal(text, FormWriter.Write(result.Document!));
    }

    private static FormDocument Parse(string text, string file)
    {
        var result = FormParser.Parse(text, file, Catalog);
        Assert.False(result.HasSyntaxErrors, string.Join("; ", result.Diagnostics));
        return result.Document!;
    }
}
