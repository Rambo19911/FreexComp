using FreexComp.Compiler.Units;

namespace FreexComp.Compiler.Tests.Units;

/// <summary>
/// C-068: LANG-510 file lookup for one candidate unit name over <see cref="IFileSystem"/>: (1) <c>in 'path'</c>, (2) units already
/// part of the compilation, (3) the main file's directory, (4) <c>-U</c> directories in order, (5) the RTL root and its immediate
/// sub-directories in ordinal name order; <c>&lt;Name&gt;.pas</c> case-insensitively, dotted names not mapped to sub-directories.
/// </summary>
[Trait("Phase", "2")]
[Trait("Req", "LANG-510")]
public sealed class UnitResolverTests
{
    private const string Main = @"C:\app\P.pas";
    private const string Rtl = @"C:\rtl";

    private static UnitResolver Resolver(InMemoryFileSystem fs, string[]? searchPaths = null, string rtl = Rtl, string[]? compilationFiles = null) =>
        new(fs, Main, [.. searchPaths ?? []], rtl, compilationFiles ?? []);

    private static InMemoryFileSystem Files(params string[] paths)
    {
        var fs = new InMemoryFileSystem().Add(Main, "program P; begin end.");
        foreach (var path in paths)
        {
            fs.Add(path, "unit X; interface implementation end.");
        }

        return fs;
    }

    [Fact]
    public void NotFound_IsNull() =>
        Assert.Null(Resolver(Files(@"C:\app\Other.pas")).Resolve("Missing"));

    [Fact]
    public void MainDirectory_IsSearched() =>
        Assert.Equal(@"C:\app\A.pas", Resolver(Files(@"C:\app\A.pas", @"C:\lib\A.pas"), [@"C:\lib"]).Resolve("A"));

    [Fact]
    public void SearchPaths_AreSearchedInOrderAfterTheMainDirectory() =>
        Assert.Equal(@"C:\lib2\A.pas", Resolver(Files(@"C:\lib2\A.pas", @"C:\lib3\A.pas"), [@"C:\lib1", @"C:\lib2", @"C:\lib3"]).Resolve("A"));

    [Fact]
    public void SearchPaths_WinOverTheRtlRoot() =>
        Assert.Equal(@"C:\lib\System.pas", Resolver(Files(@"C:\lib\System.pas", @"C:\rtl\System.pas"), [@"C:\lib"]).Resolve("System"));

    [Fact]
    public void RtlRoot_IsSearchedBeforeItsSubDirectories() =>
        Assert.Equal(@"C:\rtl\A.pas", Resolver(Files(@"C:\rtl\A.pas", @"C:\rtl\sub\A.pas")).Resolve("A"));

    /// <summary>Immediate sub-directories in ordinal name order (upper-case letters sort before lower-case ones).</summary>
    [Fact]
    public void RtlSubDirectories_AreSearchedInOrdinalNameOrder()
    {
        var fs = Files(@"C:\rtl\b\A.pas", @"C:\rtl\Z\A.pas", @"C:\rtl\a\B.pas");
        Assert.Equal(@"C:\rtl\Z\A.pas", Resolver(fs).Resolve("A"));
        Assert.Equal(@"C:\rtl\a\B.pas", Resolver(fs).Resolve("B"));
    }

    [Fact]
    public void RtlSubDirectories_OnlyImmediateOnesAreSearched() =>
        Assert.Null(Resolver(Files(@"C:\rtl\sub\deeper\A.pas")).Resolve("A"));

    /// <summary>
    /// C-184: <c>&lt;rtl root&gt;\..\winapi\src</c> (the Win32 binding units, like the CMP-620 <c>.def</c> lookup) is searched after
    /// the RTL root and its immediate sub-directories, so <c>uses Windows</c> resolves without <c>-U</c>.
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-620")]
    public void WinapiSourceFolderIsSearched()
    {
        Assert.Equal(@"C:\winapi\src\Windows.pas", Resolver(Files(@"C:\winapi\src\Windows.pas")).Resolve("Windows"));
        Assert.Equal(@"C:\rtl\sub\Windows.pas", Resolver(Files(@"C:\winapi\src\Windows.pas", @"C:\rtl\sub\Windows.pas")).Resolve("Windows"));
        Assert.Equal(@"C:\lib\Windows.pas", Resolver(Files(@"C:\winapi\src\Windows.pas", @"C:\lib\Windows.pas"), [@"C:\lib"]).Resolve("Windows"));
        Assert.Null(Resolver(Files(@"C:\winapi\src\Windows.pas"), rtl: "").Resolve("Windows"));
    }

    [Fact]
    public void NoRtlRoot_OnlyTheOtherDirectoriesAreSearched() =>
        Assert.Null(Resolver(Files(@"C:\rtl\A.pas"), rtl: "").Resolve("A"));

    [Fact]
    public void FileName_IsMatchedCaseInsensitively() =>
        Assert.True(PathUtilEquals(@"C:\app\utils.pas", Resolver(Files(@"C:\app\utils.pas")).Resolve("UTILS")));

    [Fact]
    public void DottedName_IsAFileNameNotASubDirectory()
    {
        Assert.Equal(@"C:\app\Foo.Bar.pas", Resolver(Files(@"C:\app\Foo.Bar.pas", @"C:\app\Foo\Bar.pas")).Resolve("Foo.Bar"));
        Assert.Null(Resolver(Files(@"C:\app\Foo\Bar.pas")).Resolve("Foo.Bar"));
    }

    [Fact]
    [Trait("Req", "LANG-502")]
    public void InPath_IsRelativeToTheProgramDirectoryAndWinsOverEverything() =>
        Assert.Equal(
            @"C:\app\src\Util.pas",
            Resolver(Files(@"C:\app\Util.pas", @"C:\app\src\Util.pas"), compilationFiles: [@"C:\other\Util.pas"]).Resolve("Util", @"src/Util.pas"));

    [Fact]
    [Trait("Req", "LANG-502")]
    public void InPath_MayLeaveTheProgramDirectoryOrBeAbsolute()
    {
        var fs = Files(@"C:\shared\Util.pas", @"D:\x\Any Name.pas");
        Assert.Equal(@"C:\shared\Util.pas", Resolver(fs).Resolve("Util", @"..\shared\Util.pas"));
        Assert.Equal(@"D:\x\Any Name.pas", Resolver(fs).Resolve("Util", @"D:\x\Any Name.pas"));
    }

    /// <summary>A missing <c>in</c> file is not found: the lookup does not fall back to the directories (the caller reports E2202).</summary>
    [Fact]
    [Trait("Req", "LANG-502")]
    public void InPath_MissingFile_IsNotFound() =>
        Assert.Null(Resolver(Files(@"C:\app\Util.pas")).Resolve("Util", "lib/Util.pas"));

    [Fact]
    [Trait("Req", "LANG-502")]
    public void InPath_OpenFileNotOnDisk_IsFound() =>
        Assert.Equal(@"C:\app\lib\Util.pas", Resolver(Files(), compilationFiles: [@"C:\app\lib\Util.pas"]).Resolve("Util", @"lib\Util.pas"));

    /// <summary>Step 2: a unit already part of the compilation (an open file, a project file) beats every directory.</summary>
    [Fact]
    public void CompilationFiles_WinOverTheDirectories() =>
        Assert.Equal(
            @"C:\proj\units\A.pas",
            Resolver(Files(@"C:\app\A.pas"), compilationFiles: [@"C:\proj\units\B.pas", @"C:\proj\units\A.pas"]).Resolve("a"));

    [Fact]
    public void CompilationFiles_InTheirGivenOrder() =>
        Assert.Equal(@"C:\one\A.pas", Resolver(Files(), compilationFiles: [@"C:\one\A.pas", @"C:\two\A.pas"]).Resolve("A"));

    /// <summary>An open file in a searched directory counts even when it does not exist on disk yet.</summary>
    [Fact]
    public void OpenFileInASearchedDirectory_IsFound() =>
        Assert.Equal(@"C:\lib\New.pas", Resolver(Files(), [@"C:\lib"], compilationFiles: [@"C:\lib\New.pas"]).Resolve("New"));

    /// <summary>The main file is never its own unit candidate (a main <c>System.pas</c> finds the RTL's System).</summary>
    [Fact]
    public void MainFile_IsNeverACandidate()
    {
        var fs = new InMemoryFileSystem().Add(@"C:\app\System.pas", "unit System; interface implementation end.").Add(@"C:\rtl\System.pas", "");
        var resolver = new UnitResolver(fs, @"C:\app\System.pas", [], Rtl, [@"C:\app\System.pas"]);
        Assert.Equal(@"C:\rtl\System.pas", resolver.Resolve("System"));
    }

    [Fact]
    public void Paths_AreNormalised() =>
        Assert.Equal(@"C:\lib\A.pas", Resolver(Files(@"C:\lib\A.pas"), [@"c:/lib/"]).Resolve("A"));

    /// <summary>C-164: a found file is reported with its on-disk spelling, not the spelling written in the uses clause.</summary>
    [Fact]
    public void FoundFile_HasItsOnDiskCasing()
    {
        Assert.Equal(@"C:\app\Utils.Text.pas", Resolver(Files(@"C:\app\Utils.Text.pas")).Resolve("UTILS.text"));
        Assert.Equal(@"C:\lib\Utils.pas", Resolver(Files(@"C:\lib\Utils.pas"), [@"C:\lib"]).Resolve("utils"));
        Assert.Equal(@"C:\app\SUB\Utils.pas", Resolver(Files(@"C:\app\sub\Utils.pas")).Resolve("Utils", @"SUB\utils.PAS"));     // file name only
        Assert.Equal(@"C:\lib\new.pas", Resolver(Files(), [@"C:\lib"], compilationFiles: [@"C:\lib\new.pas"]).Resolve("NEW"));       // as given
    }

    private static UnitResolver Lang535(InMemoryFileSystem fs, string aliases = "{\"aliases\":{}}", string[]? scopes = null)
    {
        fs.Add(@"C:\rtl\unit-aliases.json", aliases);
        var table = UnitAliasTable.Load(fs, Rtl, out var error);
        Assert.Null(error);
        return new UnitResolver(fs, Main, [], Rtl, [], table, [.. scopes ?? []]);
    }

    private const string StandardAliases = """{ "aliases": { "System.SysUtils": "SysUtils", "Winapi.Windows": "Windows", "Winapi.Msgs": "Messages" } }""";

    /// <summary>LANG-535 step 3: an alias key resolves to its canonical name's file.</summary>
    [Fact]
    [Trait("Req", "LANG-535")]
    public void Alias_ResolvesToTheCanonicalUnit() =>
        Assert.Equal(@"C:\rtl\SysUtils.pas", Lang535(Files(@"C:\rtl\SysUtils.pas"), StandardAliases).Find("system.SYSUTILS"));

    /// <summary>Step 2 before step 3: a user unit of the alias's spelling wins over the alias.</summary>
    [Fact]
    [Trait("Req", "LANG-535")]
    public void Alias_ExactUnitOfTheSameSpellingWins() =>
        Assert.Equal(@"C:\app\System.SysUtils.pas", Lang535(Files(@"C:\rtl\SysUtils.pas", @"C:\app\System.SysUtils.pas"), StandardAliases).Find("System.SysUtils"));

    [Fact]
    [Trait("Req", "LANG-535")]
    public void Alias_CanonicalNotFound_IsNotFound() =>
        Assert.Null(Lang535(Files(@"C:\rtl\Other.pas"), StandardAliases).Find("System.SysUtils"));

    /// <summary>Step 1: an <c>in</c> entry names its file only; a missing file does not fall through to the alias.</summary>
    [Fact]
    [Trait("Req", "LANG-535")]
    public void Alias_IsNotUsedForAnInPathEntry() =>
        Assert.Null(Lang535(Files(@"C:\rtl\SysUtils.pas"), StandardAliases).Find("System.SysUtils", "missing.pas"));

    [Fact]
    [Trait("Req", "LANG-535")]
    public void Alias_MissingTable_MeansNoAliases()
    {
        var fs = Files(@"C:\rtl\SysUtils.pas");
        Assert.Null(new UnitResolver(fs, Main, [], Rtl, [], UnitAliasTable.Load(fs, Rtl, out _), []).Find("System.SysUtils"));
    }

    /// <summary>Step 4: each scope name in order, <c>S.N</c> exactly.</summary>
    [Fact]
    [Trait("Req", "LANG-535")]
    public void ScopeNames_AreTriedInOrder() =>
        Assert.Equal(@"C:\app\Lib.Tools.pas", Lang535(Files(@"C:\app\Lib.Tools.pas", @"C:\app\Other.Tools.pas"), scopes: ["None", "Lib", "Other"]).Find("Tools"));

    [Fact]
    [Trait("Req", "LANG-535")]
    public void ScopeNames_ExactNameWins() =>
        Assert.Equal(@"C:\app\Tools.pas", Lang535(Files(@"C:\app\Lib.Tools.pas", @"C:\app\Tools.pas"), scopes: ["Lib"]).Find("Tools"));

    /// <summary>Step 3 before step 4: an alias of the written name beats a scope-name candidate.</summary>
    [Fact]
    [Trait("Req", "LANG-535")]
    public void ScopeNames_AliasOfTheWrittenNameWins() =>
        Assert.Equal(@"C:\rtl\SysUtils.pas", Lang535(Files(@"C:\rtl\SysUtils.pas", @"C:\app\X.System.SysUtils.pas"), StandardAliases, ["X"]).Find("System.SysUtils"));

    /// <summary>Step 4: <c>S.N</c> exactly, then <c>S.N</c> as an alias, before the next scope name.</summary>
    [Fact]
    [Trait("Req", "LANG-535")]
    public void ScopeNames_ScopedAlias_IsTriedBeforeTheNextScope()
    {
        var fs = Files(@"C:\rtl\Messages.pas", @"C:\app\Later.Msgs.pas");
        Assert.Equal(@"C:\rtl\Messages.pas", Lang535(fs, StandardAliases, ["Winapi", "Later"]).Find("Msgs"));
        Assert.Equal(@"C:\app\Later.Msgs.pas", Lang535(fs, StandardAliases, ["Later", "Winapi"]).Find("Msgs"));
    }

    [Fact]
    [Trait("Req", "LANG-535")]
    public void ScopeNames_NothingFound_IsNull() =>
        Assert.Null(Lang535(Files(@"C:\app\Lib.Other.pas"), StandardAliases, ["Lib", "Winapi"]).Find("Tools"));

    private static bool PathUtilEquals(string expected, string? actual) => actual is not null && Compiler.Text.PathUtil.AreEqual(expected, actual);
}
