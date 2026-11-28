using FreexComp.Ide.Core.Debugging;
using FreexComp.Ide.Core.Platform;

namespace FreexComp.Ide.Core.Tests.Platform;

/// <summary>
/// IDE-P3-099 (F14-047): <see cref="CommandLine.Join"/> builds a command-line tail by the system's argument rules, the
/// inverse of <see cref="DebugService.SplitArguments"/> — the debug console hands the adapter's argument list to the
/// console host as one tail.
/// </summary>
[Trait("Phase", "8")]
public sealed class CommandLineTests
{
    public static TheoryData<string[]> Lists => new()
    {
        { new[] { "plain", "words" } },
        { new[] { "" } },
        { new[] { "", "", "x" } },
        { new[] { "with space", "two  spaces " } },
        { new[] { "tab\there", "\t" } },
        { new[] { "quote\"inside", "\"", "\"\"", "\"lead", "trail\"" } },
        { new[] { @"trail\", @"trail\\", @"spaced trail\", @"spaced trail\\" } },
        { new[] { @"a\""b", @"a\\""b", @"\""", @"x \"" y" } },
        { new[] { @"C:\Program Files\x\", @"\\server\share", @"back\slash" } },
        { new[] { "Grüße", "✓ check", "日本語 テキスト" } },
        { new[] { "--x=1 2", "-", "--", "a=b", "new\nline" } },
        { Array.Empty<string>() },
    };

    [Theory(Timeout = 30000)]
    [MemberData(nameof(Lists))]
    [Trait("Req", "IDE-P3-099")]
    public async Task Join_RoundTripsThroughSplitArguments(string[] arguments)
    {
        await Task.Yield();
        var text = CommandLine.Join(arguments);
        Assert.Equal(arguments, DebugService.SplitArguments(text));
    }

    [Theory(Timeout = 30000)]
    [InlineData("a b", "a", "b")]
    [InlineData("\"a b\"", "a b")]
    [InlineData("\"\"", "")]
    [InlineData("\"c\\\"d\"", "c\"d")]
    [InlineData("e\\", "e\\")]
    [InlineData("\"e f\\\\\"", "e f\\")]
    [InlineData("\"g\\\\\\\"h\"", "g\\\"h")]
    [InlineData("\"--x=1 2\" Grüße", "--x=1 2", "Grüße")]
    [Trait("Req", "IDE-P3-099")]
    public async Task Join_ExactText(string expected, params string[] arguments)
    {
        await Task.Yield();
        Assert.Equal(expected, CommandLine.Join(arguments));
    }
}
