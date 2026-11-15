using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Build;

[Trait("Phase", "3")]
public sealed class ToolchainLocatorTests
{
    private const string IdeDir = @"C:\repo\ide\src\FreexComp.Ide\bin";
    private readonly InMemoryFileSystem _fs = new();

    /// <summary>IDE-SET-050: setting → FREEXCOMP_HOME → IDE dir → nearest ancestor → PATH (LLVM only); first hit wins.</summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P3-010")]
    [Trait("Req", "IDE-SET-050")]
    public void ResolutionOrder()
    {
        var services = TestComposition.Build(o => o.FileSystem = _fs);
        var settings = services.Settings;
        var locator = new ToolchainLocator(settings, _fs, new ToolchainEnvironment(@"D:\home", IdeDir, [@"E:\tools", @"F:\llvm\bin"]));

        Assert.Equal(new ToolchainPaths(null, null, null), locator.Resolve()); // nothing anywhere

        Llvm(@"F:\llvm\bin");
        Assert.Equal(@"F:\llvm\bin", locator.Resolve().LlvmBinDir); // PATH, the first directory with clang.exe

        Llvm(Repo(@"C:\repo"));
        Rtl(@"C:\repo\rtl\src");
        Framework(@"C:\repo\ui-framework");
        Assert.Equal(new ToolchainPaths(Repo(@"C:\repo"), @"C:\repo\rtl\src", @"C:\repo\ui-framework"), locator.Resolve());

        Llvm(Repo(@"C:\repo\ide\src")); // a nearer ancestor wins
        Assert.Equal(Repo(@"C:\repo\ide\src"), locator.Resolve().LlvmBinDir);

        Llvm(IdeDir + @"\llvm\bin");
        Rtl(IdeDir + @"\rtl\src");
        Framework(IdeDir + @"\ui-framework");
        Assert.Equal(new ToolchainPaths(IdeDir + @"\llvm\bin", IdeDir + @"\rtl\src", IdeDir + @"\ui-framework"), locator.Resolve());

        Llvm(@"D:\home\llvm\bin");
        Rtl(@"D:\home\rtl\src");
        Framework(@"D:\home\ui-framework");
        Assert.Equal(new ToolchainPaths(@"D:\home\llvm\bin", @"D:\home\rtl\src", @"D:\home\ui-framework"), locator.Resolve());

        // An explicit setting wins even when the folder is not valid (validation reports it); the automatic path stays
        // available for the Options placeholder.
        settings.Set(SettingsCatalog.ToolchainLlvmBinDir, @"G:\mine");
        settings.Set(SettingsCatalog.ToolchainRtlDir, @"G:\rtl");
        Assert.Equal(new ToolchainPaths(@"G:\mine", @"G:\rtl", @"D:\home\ui-framework"), locator.Resolve());
        Assert.Equal(@"D:\home\llvm\bin", locator.ResolveAutomatic().LlvmBinDir);

        // A candidate that exists but lacks the marker file is no hit.
        var bare = new ToolchainLocator(settings, _fs, new ToolchainEnvironment(null, @"H:\x", []));
        _fs.CreateDirectory(@"H:\x\llvm\bin");
        Assert.Null(bare.ResolveAutomatic().LlvmBinDir);
    }

    /// <summary>IDE-P3-011/012: the first missing file; Check Syntax needs only the RTL.</summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P3-011")]
    [Trait("Req", "IDE-P3-012")]
    public void MissingToolchainBlocksBuildWithMessage()
    {
        var services = TestComposition.Build(o => o.FileSystem = _fs);
        var locator = new ToolchainLocator(services.Settings, _fs, new ToolchainEnvironment(null, IdeDir, []));

        var none = locator.CheckForBuild(needsLlvm: true);
        Assert.False(none.Success);
        Assert.Equal("Msg.Build.ToolchainMissing", none.MessageId);
        Assert.Equal(["clang.exe"], none.Args);

        services.Settings.Set(SettingsCatalog.ToolchainLlvmBinDir, @"G:\llvm");
        Touch(@"G:\llvm\clang.exe");
        Touch(@"G:\llvm\lld-link.exe");
        Assert.Equal([@"G:\llvm\llvm-dlltool.exe"], locator.CheckForBuild(needsLlvm: true).Args);

        Touch(@"G:\llvm\llvm-dlltool.exe");
        Assert.Equal(["System.pas"], locator.CheckForBuild(needsLlvm: true).Args); // no RTL found automatically
        services.Settings.Set(SettingsCatalog.ToolchainRtlDir, @"G:\rtl");
        Assert.Equal([@"G:\rtl\System.pas"], locator.CheckForBuild(needsLlvm: false).Args);

        Touch(@"G:\rtl\System.pas");
        Assert.True(locator.CheckForBuild(needsLlvm: true).Success);
        services.Settings.Set(SettingsCatalog.ToolchainLlvmBinDir, @"G:\gone");
        Assert.False(locator.CheckForBuild(needsLlvm: true).Success);
        Assert.True(locator.CheckForBuild(needsLlvm: false).Success); // project.checkSyntax needs only the RTL
    }

    /// <summary>dialogs.md §9.9: field status for an explicit or automatic path.</summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-092")]
    public void FolderValidationMessages()
    {
        _fs.CreateDirectory(@"G:\llvm");
        Touch(@"G:\llvm\clang.exe");

        Assert.Equal("Msg.Options.PathNotFound", ToolchainLocator.ValidateFolder(_fs, @"G:\nope", ToolchainLocator.LlvmFiles).MessageId);
        var lacking = ToolchainLocator.ValidateFolder(_fs, @"G:\llvm", ToolchainLocator.LlvmFiles);
        Assert.Equal("Msg.Options.ToolNotFound", lacking.MessageId);
        Assert.Equal(["lld-link.exe"], lacking.Args);
        Touch(@"G:\llvm\lld-link.exe");
        Touch(@"G:\llvm\llvm-dlltool.exe");
        var found = ToolchainLocator.ValidateFolder(_fs, @"G:\llvm", ToolchainLocator.LlvmFiles);
        Assert.True(found.Valid);
        Assert.Equal("Msg.Options.ToolchainFound", found.MessageId);
        Assert.Equal(["clang.exe, lld-link.exe, llvm-dlltool.exe"], found.Args);
    }

    /// <summary>dialogs.md §9.9: placeholders name the automatic folders; statuses and errors follow the effective folder.</summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-SET-050")]
    public void ToolchainPathsPageFields()
    {
        var services = TestComposition.Build(o => o.FileSystem = _fs);
        Llvm(@"D:\home\llvm\bin");
        Touch(@"D:\home\llvm\bin\lld-link.exe");
        Touch(@"D:\home\llvm\bin\llvm-dlltool.exe");
        var locator = new ToolchainLocator(services.Settings, _fs, new ToolchainEnvironment(@"D:\home", IdeDir, []));
        var options = new Core.Dialogs.OptionsViewModel(services.Settings, services.Keymap, services.Layout, services.Dialogs, services.FilePrompts,
            Core.Platform.ThemeKind.Light, Core.Dialogs.OptionsViewModel.ToolchainPathsPage, implementedPhase: 3, locator, _fs);

        var llvm = options.Field("LlvmBinDir");
        Assert.Equal(@"(automatic: D:\home\llvm\bin)", llvm.Placeholder);
        Assert.Equal("clang.exe, lld-link.exe, llvm-dlltool.exe found", llvm.Status);
        Assert.False(llvm.StatusIsWarning);
        var rtl = options.Field("RtlDir");
        Assert.Equal("(automatic: not found)", rtl.Placeholder);
        Assert.True(rtl.StatusIsWarning);
        Assert.Equal(Core.Resources.Strings.Msg_Options_ToolchainAutoNotFound, rtl.Status);
        Assert.Null(rtl.Error); // a failing automatic folder is a warning, not an error

        rtl.Text = @"G:\nope";
        Assert.Equal(Core.Resources.Strings.Msg_Options_PathNotFound, rtl.Error);
        Assert.True(options.HasErrors); // blocks OK
        Rtl(@"G:\rtl");
        _fs.WriteAllBytes(@"G:\rtl\Other.pas", []);
        rtl.Text = @"G:\rtl";
        Assert.Null(rtl.Error);
        Assert.Equal("System.pas found", rtl.Status);
        llvm.Text = @"G:\rtl";
        Assert.Equal("clang.exe was not found in this folder.", llvm.Error);
        llvm.Text = "";
        Assert.Null(llvm.Error);
        Assert.False(options.HasErrors);
    }

    /// <summary>dialogs.md §15: the About dialog's Toolchain field shows the located LLVM folder.</summary>
    [Fact(Timeout = 30000)]
    public void AboutShowsTheLocatedToolchain()
    {
        var services = TestComposition.Build(o =>
        {
            o.FileSystem = _fs;
            o.ToolchainEnvironment = new ToolchainEnvironment(@"D:\home", IdeDir, []);
        });
        var help = (Core.Services.HelpService)services.Help;
        Assert.Null(help.ToolchainText());

        foreach (var file in ToolchainLocator.LlvmFiles)
            Touch(@"D:\home\llvm\bin\" + file);
        Assert.Equal(@"LLVM — D:\home\llvm\bin", help.ToolchainText());
        Assert.Equal(@"D:\home\llvm\bin", services.Toolchain.Resolve().LlvmBinDir);
    }

    private static string Repo(string root) => Path.Combine(root, ToolchainLocator.RepositoryLlvmDir);

    private void Touch(string path)
    {
        _fs.CreateDirectory(Path.GetDirectoryName(path)!);
        _fs.WriteAllBytes(path, []);
    }

    private void Llvm(string dir) => Touch(dir + @"\clang.exe");

    private void Rtl(string dir) => Touch(dir + @"\System.pas");

    private void Framework(string dir) => Touch(dir + @"\design\index.json");
}
