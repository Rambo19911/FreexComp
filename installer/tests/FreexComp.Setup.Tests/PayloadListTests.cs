namespace FreexComp.Setup.Tests;

/// <summary>
/// INS-002: the payload lists (<c>installer/payload-*.txt</c>). Globs are relative to a base folder; <c>*</c> and <c>?</c>
/// stay inside one folder, <c>**</c> crosses folders; a line starting with <c>!</c> takes files out again; a line that
/// selects or removes nothing is an error. The rules are checked on small trees, the repository's own lists on the real
/// third-party folders (present after the bootstrap).
/// </summary>
[Trait("Phase", "8")]
public sealed class PayloadListTests
{
    private static readonly string[] Tree =
    [
        "bin/clang.exe", "bin/lld-link.exe", "bin/lldb-dap.exe", "bin/liblldb.dll", "bin/tool.pdb", "bin/sub/deep.dll",
        "lib/site-packages/lldb/__init__.py", "lib/site-packages/lldb/_lldb.pyd", "lib/clang/23/include/stddef.h",
        "LICENSE.TXT", "a.b/c+d (1).txt",
    ];

    private static string MakeTree(SetupSandbox box, string name = "tree", IEnumerable<string>? files = null) =>
        box.Payload(name, (files ?? Tree).ToDictionary(f => f.Replace('/', '\\'), f => "content of " + f));

    private static string List(SetupSandbox box, params string[] lines)
    {
        var file = box.In("list-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllLines(file, lines);
        return file;
    }

    private static IReadOnlyList<string> Resolve(SetupSandbox box, params string[] lines) =>
        PayloadList.Resolve(List(box, lines), MakeTree(box));

    [Theory]
    [Trait("Req", "INS-002")]
    // a plain path names one file; separators may be written either way; names compare ignoring case
    [InlineData("bin/clang.exe", @"bin\clang.exe")]
    [InlineData(@"bin\clang.exe", @"bin\clang.exe")]
    [InlineData("BIN/Clang.EXE", @"bin\clang.exe")]
    // * and ? stay inside one folder
    [InlineData("bin/*.exe", @"bin\clang.exe|bin\lld-link.exe|bin\lldb-dap.exe")]
    [InlineData("bin/*", @"bin\clang.exe|bin\liblldb.dll|bin\lld-link.exe|bin\lldb-dap.exe|bin\tool.pdb")]
    [InlineData("bin/*.dll", @"bin\liblldb.dll")]              // not bin\sub\deep.dll
    [InlineData("bin/lld?-dap.exe", @"bin\lldb-dap.exe")]
    [InlineData("bin/clang?exe", @"bin\clang.exe")]             // ? is exactly one character, and the dot is one
    [InlineData("*", "LICENSE.TXT")]
    [InlineData("*/*.dll", @"bin\liblldb.dll")]
    // ** crosses folders; "**/" also stands for no folder at all
    [InlineData("bin/**", @"bin\clang.exe|bin\liblldb.dll|bin\lld-link.exe|bin\lldb-dap.exe|bin\sub\deep.dll|bin\tool.pdb")]
    [InlineData("**/*.dll", @"bin\liblldb.dll|bin\sub\deep.dll")]
    [InlineData("**/LICENSE.TXT", "LICENSE.TXT")]
    [InlineData("lib/**/*.h", @"lib\clang\23\include\stddef.h")]
    [InlineData("lib/site-packages/**", @"lib\site-packages\lldb\_lldb.pyd|lib\site-packages\lldb\__init__.py")]
    // every other character stands for itself (no regular-expression meaning leaks through)
    [InlineData("a.b/c+d (1).txt", @"a.b\c+d (1).txt")]
    public void GlobSelectsFiles(string glob, string expected)
    {
        using var box = new SetupSandbox();
        Assert.Equal(expected.Split('|'), Resolve(box, glob));
    }

    [Fact]
    [Trait("Req", "INS-002")]
    public void ListIsSortedWithoutDuplicatesAndIgnoresCommentsAndBlankLines()
    {
        using var box = new SetupSandbox();
        var files = Resolve(box,
            "# a comment line",
            "",
            "   bin/lldb-dap.exe   # the adapter",
            "bin/*.exe",
            "\tLICENSE.TXT",
            "bin/clang.exe");
        Assert.Equal([@"bin\clang.exe", @"bin\lld-link.exe", @"bin\lldb-dap.exe", "LICENSE.TXT"], files);
    }

    [Theory]
    [Trait("Req", "INS-002")]
    [InlineData("bin/clang.exe|bin/missing.exe", "'bin/missing.exe' matches no file")]
    [InlineData("bin/*.so", "'bin/*.so' matches no file")]
    [InlineData("bin/sub", "'bin/sub' matches no file")]                 // a folder is not a file
    [InlineData("clang.exe", "'clang.exe' matches no file")]             // globs are anchored at the base folder
    [InlineData("bin/clang", "'bin/clang' matches no file")]             // and at the end of the name
    [InlineData("# only a comment|", "names no file")]
    public void GlobThatMatchesNothingIsAnError(string lines, string message)
    {
        using var box = new SetupSandbox();
        var list = List(box, lines.Split('|'));
        var error = Assert.Throws<InvalidDataException>(() => PayloadList.Resolve(list, MakeTree(box)));
        Assert.Contains(message, error.Message, StringComparison.Ordinal);
        Assert.Contains(list, error.Message, StringComparison.Ordinal);

        // nothing is copied when the list is wrong
        var target = box.In("copied");
        Assert.Throws<InvalidDataException>(() => PayloadList.Copy(list, MakeTree(box), target));
        Assert.False(Directory.Exists(target));
    }

    [Fact]
    [Trait("Req", "INS-002")]
    public void ExclusionTakesFilesOutAgain()
    {
        using var box = new SetupSandbox();
        // wherever the exclusion stands, it applies to the whole selection
        Assert.Equal([@"bin\clang.exe", @"bin\liblldb.dll", @"bin\lld-link.exe", @"bin\lldb-dap.exe", @"bin\sub\deep.dll"],
            Resolve(box, "!**/*.pdb   # no debug symbols", "bin/**"));
        Assert.Equal([@"bin\clang.exe", @"bin\liblldb.dll", @"bin\lld-link.exe", @"bin\lldb-dap.exe", @"bin\sub\deep.dll"],
            Resolve(box, "bin/**", "!**/*.PDB"));
        Assert.Equal([@"lib\clang\23\include\stddef.h"], Resolve(box, "lib/**", "! lib/site-packages/**"));
    }

    [Theory]
    [Trait("Req", "INS-002")]
    [InlineData("bin/*.exe|!**/*.pdb", "'!**/*.pdb' excludes no selected file")]     // the tree has one, the selection none
    [InlineData("**|!**/*.map", "'!**/*.map' excludes no selected file")]
    [InlineData("!**/*.pdb", "names no file")]                                        // exclusions alone select nothing
    [InlineData("bin/*.pdb|!**/*.pdb", "selects no file")]                            // everything selected is excluded again
    [InlineData("bin/clang.exe|!", "'!' is not a rule")]
    public void ExclusionThatRemovesNothingIsAnError(string lines, string message)
    {
        using var box = new SetupSandbox();
        var list = List(box, lines.Split('|'));
        var error = Assert.Throws<InvalidDataException>(() => PayloadList.Resolve(list, MakeTree(box)));
        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "INS-002")]
    public void CopyKeepsRelativePathsAndContent()
    {
        using var box = new SetupSandbox();
        var tree = MakeTree(box);
        var target = box.In("copied");
        Assert.Equal(3, PayloadList.Copy(List(box, "bin/sub/*", "lib/site-packages/**"), tree, target));
        var copied = Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(target, f)).Order(StringComparer.OrdinalIgnoreCase).ToList();
        Assert.Equal([@"bin\sub\deep.dll", @"lib\site-packages\lldb\_lldb.pyd", @"lib\site-packages\lldb\__init__.py"], copied);
        foreach (var path in copied)
            Assert.Equal(File.ReadAllBytes(Path.Combine(tree, path)), File.ReadAllBytes(Path.Combine(target, path)));
    }

    /// <summary>
    /// The list of the published program files (<c>installer/payload-programs.txt</c>) takes everything the four programs
    /// publish except debug symbol files: the setup and the portable zip ship no <c>.pdb</c> (lead decision, L-045).
    /// </summary>
    [Fact]
    [Trait("Req", "INS-002")]
    public void ProgramListShipsNoDebugSymbols()
    {
        using var box = new SetupSandbox();
        var published = MakeTree(box, "published",
        [
            "FreexComp.Ide.exe", "FreexComp.Ide.dll", "FreexComp.Ide.pdb", "FreexComp.Compiler.dll", "FreexComp.Compiler.PDB",
            "opc.exe", "opc.pdb", "opc.deps.json", "licenses/LICENSE", "zh-Hans/Resources.dll", "zh-Hans/Resources.pdb", "NETWORK-POLICY.md",
        ]);
        var files = PayloadList.Resolve(RepositoryList("payload-programs.txt"), published);
        Assert.Equal(
            ["FreexComp.Compiler.dll", "FreexComp.Ide.dll", "FreexComp.Ide.exe", @"licenses\LICENSE", "NETWORK-POLICY.md", "opc.deps.json", "opc.exe", @"zh-Hans\Resources.dll"],
            files);

        // a build that writes no symbol files any more makes the rule stale, which is reported (not silently accepted)
        var withoutSymbols = MakeTree(box, "published-without-symbols", ["opc.exe", "opc.dll"]);
        var error = Assert.Throws<InvalidDataException>(() => PayloadList.Resolve(RepositoryList("payload-programs.txt"), withoutSymbols));
        Assert.Contains("excludes no selected file", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The third-party lists select run-time files only: no debug symbols, and nothing of the build inputs that are not
    /// shipped (the Unicode data file, from which the character tables of the runtime library are generated).
    /// </summary>
    [Fact]
    [Trait("Req", "INS-002")]
    public void ThirdPartyListsSelectRunTimeFilesOnly()
    {
        var thirdParty = Path.Combine(SetupProgramBuild.RepositoryRoot(), "third_party");
        Assert.True(Directory.Exists(Path.Combine(thirdParty, "llvm", "bin")), thirdParty + @"\llvm\bin is missing (run scripts\bootstrap.ps1 once)");
        Assert.True(File.Exists(Path.Combine(thirdParty, "unicode", "UnicodeData.txt")), "the Unicode data file (build input) is missing: the check below would prove nothing");

        var llvm = PayloadList.Resolve(RepositoryList("payload-llvm.txt"), Path.Combine(thirdParty, "llvm"));
        var other = PayloadList.Resolve(RepositoryList("payload-third-party.txt"), thirdParty);
        Assert.Contains(@"bin\clang.exe", llvm);
        Assert.Contains(@"sqlite\sqlite3.dll", other);
        foreach (var path in llvm.Concat(other))
        {
            Assert.False(path.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase), path);
            Assert.DoesNotContain("unicode", path, StringComparison.OrdinalIgnoreCase);
        }

        // the debugger's scripting module and the extension modules of the Python package are not shipped: a debug
        // session loads none of them (EndToEndTests debugs with exactly the listed files)
        Assert.DoesNotContain(llvm, path => path.Contains("site-packages", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(llvm.Concat(other), path => path.EndsWith(".pyd", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(["LICENSE.txt", "python3.dll", "python313._pth", "python313.dll", "python313.zip", "vcruntime140.dll"],
            other.Where(path => path.StartsWith(@"python-embed\", StringComparison.OrdinalIgnoreCase)).Select(Path.GetFileName).Order(StringComparer.Ordinal));

        Assert.All(other, path => Assert.True(
            path.StartsWith(@"sqlite\", StringComparison.OrdinalIgnoreCase) || path.StartsWith(@"python-embed\", StringComparison.OrdinalIgnoreCase), path));
    }

    private static string RepositoryList(string name)
    {
        var file = Path.Combine(SetupProgramBuild.RepositoryRoot(), "installer", name);
        Assert.True(File.Exists(file), file + " is missing");
        return file;
    }
}
