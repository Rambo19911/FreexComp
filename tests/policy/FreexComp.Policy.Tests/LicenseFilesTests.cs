using System.Text;
using System.Text.RegularExpressions;

namespace FreexComp.Policy.Tests;

/// <summary>The product's own licence files in the repository root (LIC-001…005).</summary>
[Trait("Phase", "8")]
public sealed class LicenseFilesTests
{
    private const string ExceptionHeading = "---- LLVM Exceptions to the Apache 2.0 License ----";

    private const string NoticeText =
        "FreexComp\n" +
        "Copyright 2026 The FreexComp authors\n" +
        "\n" +
        "This product is licensed under the Apache License, Version 2.0 (see LICENSE).\n" +
        "\n" +
        "The runtime libraries in rtl/, ui-framework/ and data/ are licensed under\n" +
        "the Apache License, Version 2.0 with LLVM Exceptions (see LICENSE-RUNTIME).\n" +
        "\n" +
        "This product includes third-party software. Names, versions, licenses and\n" +
        "license texts are listed in THIRD-PARTY-NOTICES.txt.\n";

    private static readonly string[] LfFiles = ["LICENSE-RUNTIME", "NOTICE", "THIRD-PARTY-NOTICES.txt"];

    private static string ReadStrictUtf8(string name)
    {
        var path = RepoPaths.Combine(name);
        Assert.True(File.Exists(path), $"{name} is missing in the repository root");
        var bytes = File.ReadAllBytes(path);
        Assert.NotEmpty(bytes);
        Assert.False(bytes is [0xEF, 0xBB, 0xBF, ..], $"{name} starts with a byte order mark");
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
    }

    [Fact]
    [Trait("Req", "LIC-001")]
    public void RootFilesExistUtf8NoBomLf()
    {
        foreach (var name in (string[])["LICENSE", "THIRD-PARTY-NOTICES.md", .. LfFiles])
        {
            ReadStrictUtf8(name);
        }

        var attributes = File.ReadAllLines(RepoPaths.Combine(".gitattributes"));
        foreach (var name in LfFiles)
        {
            var text = ReadStrictUtf8(name);
            Assert.False(text.Contains('\r'), $"{name} contains a carriage return");
            Assert.True(text.EndsWith('\n') && !text.EndsWith("\n\n", StringComparison.Ordinal), $"{name} must end with exactly one LF");
            Assert.Contains($"{name} text eol=lf", attributes);
        }
    }

    [Fact]
    [Trait("Req", "LIC-002")]
    [Trait("Req", "LIC-003")]
    public void RuntimeLicenseIsProductLicensePlusLlvmException()
    {
        var product = ReadStrictUtf8("LICENSE").ReplaceLineEndings("\n");
        Assert.Contains("Apache License\n", product, StringComparison.Ordinal);
        Assert.Contains("Version 2.0, January 2004\n", product, StringComparison.Ordinal);
        Assert.EndsWith("limitations under the License.\n", product, StringComparison.Ordinal);

        var runtime = ReadStrictUtf8("LICENSE-RUNTIME");
        Assert.StartsWith(product, runtime, StringComparison.Ordinal);
        var rest = runtime[product.Length..];
        Assert.StartsWith(
            "\n" + ExceptionHeading + "\n\nAs an exception, if, as a result of your compiling your source code",
            rest,
            StringComparison.Ordinal);
        Assert.True(
            rest.Contains("\n\nIn addition, if you combine or link compiled forms of this Software", StringComparison.Ordinal),
            "the second paragraph of the exception is missing or out of order");
        Assert.Equal(1, runtime.Split(ExceptionHeading).Length - 1);

        // The heading and the two paragraphs, nothing else; one final LF.
        Assert.True(runtime.EndsWith('\n') && !runtime.EndsWith("\n\n", StringComparison.Ordinal), "must end with exactly one LF");
        var paragraphs = rest[1..^1].Split("\n\n");
        Assert.Equal(3, paragraphs.Length);
        Assert.Equal(ExceptionHeading, paragraphs[0]);
        Assert.EndsWith("4(d) of the License.", paragraphs[1], StringComparison.Ordinal);
        Assert.StartsWith("In addition, if you combine or link", paragraphs[2], StringComparison.Ordinal);
        Assert.EndsWith("with respect to the Combined\nSoftware.", paragraphs[2], StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "LIC-004")]
    public void NoticeHasExactText() => Assert.Equal(NoticeText, ReadStrictUtf8("NOTICE"));

    [Fact]
    [Trait("Req", "LIC-005")]
    public void ReadmeHasLicenseSection()
    {
        var lines = ReadStrictUtf8("README.md").ReplaceLineEndings("\n").Split('\n');
        Assert.Single(lines, l => l == "## License");
        var start = Array.IndexOf(lines, "## License") + 1;
        var section = string.Join(' ', lines.Skip(start).TakeWhile(l => !l.StartsWith('#')).Where(l => l.Length > 0));

        Assert.Contains("(`LICENSE`)", section, StringComparison.Ordinal);
        Assert.Contains("(`LICENSE-RUNTIME`)", section, StringComparison.Ordinal);
        Assert.Contains("`THIRD-PARTY-NOTICES.txt`", section, StringComparison.Ordinal);
        Assert.Contains("Apache-2.0 WITH LLVM-exception", section, StringComparison.Ordinal);
        // Exactly three sentences: a full stop followed by a space or by the end of the section.
        Assert.Equal(3, Regex.Count(section, @"\.(\s|$)"));
    }
}
