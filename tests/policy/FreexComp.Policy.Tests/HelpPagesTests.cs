using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FreexComp.Policy.Tests;

/// <summary>
/// The user help pages in <c>help/</c> (Markdown): every page is UTF-8 without a byte order mark, has LF line ends, starts
/// with its one <c>#</c> title and uses the product's own wording. The reference terms a page must not name are held as
/// SHA-256 values of their text, so this file does not spell them either.
/// </summary>
[Trait("Phase", "8")]
public sealed partial class HelpPagesTests
{
    // <terms>
    /// <summary>Reference terms matched without regard to case: length and SHA-256 of the lower-case text.</summary>
    private static readonly (int Length, string Hash)[] FoldedTerms =
    [
        (12, "06c3a4a7b3d654d9db1cb7aec925f82d142da64ebc0284e8e58122e8e5cee4be"),
        (14, "0756fb8d7c0ad79fb16d9260aed2fb236453a3cdb2310504e0924a5bd5bf839f"),
        (7, "197499a205542b6ac751dd3f7167d37dc574bc26ec63c6ec62fcbe186235af49"),
        (8, "2087cda36bfe22d92189f710a175605559cc4d0327b0ebbffb20220c15bb206a"),
        (3, "223d659d575b9bd16b1aa73bfa7a96b7e41f2e0617de8cf5cba2b3050ed65c37"),
        (5, "375082520142eb43c477caf1f04e8a7e296d97d07e74fa43eab935321dae87c0"),
        (12, "3827022d2f39217a0f8aa2d9eb27cd3ad68f176e466d08d565315c96a0749ed7"),
        (9, "3d80ea8460b6419e113ceb616db5c3605dad87d5a9926c1c739840cae0317b13"),
        (9, "3deb0edf6636671244b31a67a8ea0973b89fa479eb0a5994de53c3393625de5c"),
        (11, "4cd73ff57702ad976433998e3d007bfd74f0c5259dca97940bc488cb9454d58b"),
        (9, "5af370cd370c760e6e29604c5755b86b2235d95d920ec6dce171e4f4c49a7bf1"),
        (10, "5f56915acf9bb64d2c0b3df4bb310dfcfa28dc073ad6b05836cc679090d9db18"),
        (3, "63b4c067f39928ff2222fe2c4b7f0b065cac58b03de41e9531c42d20dbd68796"),
        (11, "6615a64df2e8c5381683fc3b5deb396d46ee233f4808e57ce30486fe50f3cef7"),
        (16, "6bd291b26949bc639a7ec9cb4e2df922786a6f7da267b4a51f762c400ea9c1ef"),
        (3, "6c29c56c74ffab97b8b9bfe630e6009922f23e7b550c6f26f9daba1c94f29066"),
        (3, "71d24c6e8724702998259d5f4071a9d565dbd24563a61a206b04a4e546eef132"),
        (13, "812f26c6b8f299fbcbc0d76093ddc542020d301d2e38bd24c59511af17decb7e"),
        (5, "8638148e480602bb130853a19f2a52ef88fef5219d173f77f4a6e77f3f4aa163"),
        (9, "9271e14e01690279369ad141a784b18f3618cded623611635059fd13731843c8"),
        (8, "94a27cc8c4f5c9e0fbdf08d5fef9df9140389478ff783ad645522ce3afbdd009"),
        (3, "9822f2b430fc000e141b918a94cbf5cd57682acc9d2dd602f28b51c236e69f0b"),
        (6, "9b7d1679cf05cef55d9a2d9c85a25c59b7ee122af423902262c2380ff7f38dfd"),
        (6, "a69ec880b9edefd54d50e869f4a03c21fb4956b08bb983c5fa0301ed1037f223"),
        (6, "a95404c7dafe18ee6ef55e690c2617244482e90c34474b6672748b1a9237eb72"),
        (8, "bac8e44005910731a9672b65c89f62eb4826d0741db8fdecd27e344e000a70ca"),
        (11, "bd38fd788f0a0b4cecbad4a0bf24a4069ae3a4d3f8d5e8a5404197674e2528dd"),
        (14, "bda64ef1d7951ea524997f05fa659f6062dcbe27ba379596a9a93f7c9bea5508"),
        (5, "c95b1f1e5ea17c035945aebedfd7546c818d2eaf82e2fdbd58a29566c85d1688"),
        (6, "cb9fa2a3b15a4d2e76d27bf339ea5f0d34623b527be80a03186b23ea42a814bf"),
        (7, "d39aa0f1f8b840d7e7383df23e8303df47e755ca089cf3625c9a7fd780ac5c65"),
        (7, "d4c5d22baf120b953ceb85a4d142f1c5fef2b002b2f1a050345ec1e90c5aeaa8"),
        (10, "d9944f8f55d1de9b6d8bb20a87f588e24e01ade0e1fc01b104c6883b300f5bbd"),
        (14, "debd6a5715fa76861f7d6037afbe1bc07cea283288af3d5a503c4c1bd28b29a3"),
        (11, "ebb24ee4b64f319c2a4743db4d6304c86f7f7d75fcfceaef73765acc1da52927"),
        (10, "ee13a9733bf2c1d417fe66fbd6761bb487929bf438277424d467a9749c0f30ba"),
        (6, "f11f78b6cf50930b3a047d8a9876fc18d5b2e96160df5be2842c6c35502e2c1a"),
        (9, "f7db1961bf18fa22c4b4f817ace607346eecae696f12efc811d2a1d0d58f9457"),
        (10, "f831039a771cc5f9becc6273b60ee70c59efaba1a47da879a6b784df9fc7958e"),
    ];

    /// <summary>Reference terms that are ordinary words in lower case: matched in exactly their case.</summary>
    private static readonly (int Length, string Hash)[] ExactTerms =
    [
        (12, "1e0ddd334c8fb3e78e5998000deb17f0b6ba60b03914e6154a985d5fac52a9d4"),
        (13, "2b87f127b9856190c779c373d8139df2d76eeeb7a6b0313bdb461944f3f1cf42"),
        (12, "51e5a17b84e8c071914c8c4bbfc7597c9927069a817f6ea6ecdd2ff83347bfc8"),
        (4, "7fba014d7faa827d85b800273a3339a4039e328e7487cd92cdb8b72b80359ac2"),
        (3, "94c8f390d7cb86ebdc1e81422d637fae406298a6efca59b2799de42b343d7cdf"),
        (6, "b307d1a8b50a8f092f393b62443997252367ab9d603d82e039087cf36e9ecab3"),
    ];
    // </terms>

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhiteSpace();

    private static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    /// <summary>
    /// The places of <paramref name="text"/> that spell a term of the two lists as whole words: <paramref name="folded"/>
    /// without regard to case, <paramref name="exact"/> in exactly its case. Each hit is returned with the text found.
    /// </summary>
    private static List<string> TermsIn(string text, IReadOnlyCollection<(int Length, string Hash)> folded, IReadOnlyCollection<(int Length, string Hash)> exact)
    {
        var hits = new List<string>();
        var plain = WhiteSpace().Replace(text, " ");
        Scan(plain.ToLowerInvariant(), folded);
        Scan(plain, exact);
        return hits;

        void Scan(string candidate, IReadOnlyCollection<(int Length, string Hash)> terms)
        {
            var hashes = terms.Select(t => t.Hash).ToHashSet(StringComparer.Ordinal);
            foreach (var length in terms.Select(t => t.Length).Distinct())
            {
                for (var at = 0; at + length <= candidate.Length; at++)
                {
                    var wholeWord = (at == 0 || !char.IsLetterOrDigit(candidate[at - 1]))
                        && (at + length == candidate.Length || !char.IsLetterOrDigit(candidate[at + length]));
                    if (wholeWord && hashes.Contains(Hash(candidate.Substring(at, length))))
                    {
                        hits.Add(plain.Substring(at, length));
                    }
                }
            }
        }
    }

    [Fact]
    public void EveryPageHasTitleAndCleanWording()
    {
        var root = RepoPaths.Combine("help");
        Assert.True(Directory.Exists(root), "the folder help/ is missing");
        var pages = Directory.GetFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToList();
        Assert.NotEmpty(pages);
        Assert.Contains(Path.Combine(root, "supported-platforms.md"), pages);
        Assert.Contains(Path.Combine(root, "libraries.md"), pages);

        var problems = new List<string>();
        foreach (var page in pages)
        {
            var name = Path.GetRelativePath(RepoPaths.Root, page).Replace('\\', '/');
            if (!name.EndsWith(".md", StringComparison.Ordinal))
            {
                problems.Add($"{name}: not a Markdown page (.md)");
                continue;
            }

            var bytes = File.ReadAllBytes(page);
            if (bytes is [0xEF, 0xBB, 0xBF, ..])
            {
                problems.Add($"{name}: starts with a byte order mark");
            }

            string text;
            try
            {
                text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                problems.Add($"{name}: not valid UTF-8");
                continue;
            }

            if (text.Contains('\r'))
            {
                problems.Add($"{name}: contains a carriage return (LF line ends only)");
            }

            if (!text.EndsWith('\n') || text.EndsWith("\n\n", StringComparison.Ordinal))
            {
                problems.Add($"{name}: must end with exactly one line feed");
            }

            // one title: the first line; a '#' line inside a fenced code block is not a heading
            var lines = text.Split('\n');
            var fenced = false;
            var titles = 0;
            foreach (var line in lines)
            {
                if (line.StartsWith("```", StringComparison.Ordinal))
                {
                    fenced = !fenced;
                }
                else if (!fenced && line.StartsWith("# ", StringComparison.Ordinal))
                {
                    titles++;
                }
            }

            if (!lines[0].StartsWith("# ", StringComparison.Ordinal) || lines[0].Trim().Length < 3)
            {
                problems.Add($"{name}: the first line must be the '# ' title");
            }

            if (titles != 1)
            {
                problems.Add($"{name}: {titles} '# ' titles, expected one");
            }

            problems.AddRange(TermsIn(text, FoldedTerms, ExactTerms).Select(hit => $"{name}: names the reference term '{hit}'; use the product's own term"));
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>The wording check finds whole words in any case, across a line break, and leaves parts of longer words alone.</summary>
    [Fact]
    public void WordingCheckFindsWholeWordsOnly()
    {
        (int, string)[] folded = [("sample term".Length, Hash("sample term")), ("c++sample".Length, Hash("c++sample"))];
        (int, string)[] exact = [("Mixed Case".Length, Hash("Mixed Case"))];

        Assert.Equal(["Sample Term"], TermsIn("A Sample\nTerm here.", folded, exact));
        Assert.Equal(["SAMPLE TERM", "C++Sample"], TermsIn("(SAMPLE TERM) and `C++Sample`", folded, exact));
        Assert.Equal(["Mixed Case"], TermsIn("Mixed Case, but not mixed case", folded, exact));
        Assert.Empty(TermsIn("resample terms, sample terms, c++samples, Mixed Cases", folded, exact));
        Assert.Empty(TermsIn("", folded, exact));
    }

    /// <summary>The lists are in use: no entry is malformed, none is doubled.</summary>
    [Fact]
    public void TermListsAreWellFormed()
    {
        foreach (var list in ((int Length, string Hash)[][])[FoldedTerms, ExactTerms])
        {
            Assert.NotEmpty(list);
            Assert.All(list, t => Assert.True(t.Length is >= 3 and <= 40 && t.Hash.Length == 64 && t.Hash.All(char.IsAsciiHexDigitLower), t.Hash));
            Assert.Equal(list.Length, list.Select(t => t.Hash).Distinct().Count());
        }
    }
}
