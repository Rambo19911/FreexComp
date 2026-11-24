using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Documents;

/// <summary>IDE-LIB-001 (I-232): the one service that says what library source is.</summary>
[Trait("Phase", "8")]
public sealed class LibrarySourceServiceTests
{
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-LIB-001")]
    public Task RootsFollowToolchainSettings() => TestUiThread.RunAsync(() =>
    {
        var services = TestComposition.Build();
        var library = services.LibrarySource;

        // The test environment finds no toolchain (IDE-P1-001): no roots, nothing is library source.
        Assert.Empty(library.Roots);
        Assert.False(library.IsLibrarySource(@"G:\rtl\System.pas"));

        // The roots are the run-time library, framework and data-access directories, normalised, in this order.
        services.Settings.Set(SettingsCatalog.ToolchainRtlDir, @"G:\rtl\");
        Assert.Equal([@"G:\rtl"], library.Roots);
        services.Settings.Set(SettingsCatalog.ToolchainFrameworkDir, @"G:/kit/ui");
        services.Settings.Set(SettingsCatalog.ToolchainDataDesignDir, @"G:\kit\data\design");
        Assert.Equal([@"G:\rtl", @"G:\kit\ui", @"G:\kit\data"], library.Roots);

        // Under a root, ignoring case and the kind of separator; the root itself and a sibling with the same prefix are not.
        Assert.True(library.IsLibrarySource(@"G:\rtl\System.pas"));
        Assert.True(library.IsLibrarySource(@"g:\RTL\sub\Deep.inc"));
        Assert.True(library.IsLibrarySource(@"G:/kit/ui/src/Forms.pas"));
        Assert.True(library.IsLibrarySource(@"G:\kit\data\src\..\src\Db.pas"));
        Assert.False(library.IsLibrarySource(@"G:\rtl"));
        Assert.False(library.IsLibrarySource(@"G:\rtl2\System.pas"));
        Assert.False(library.IsLibrarySource(@"G:\kit\Other.pas"));
        Assert.False(library.IsLibrarySource(@"C:\p\Hello\Unit1.pas"));

        // A changed setting is seen at once; a cleared one removes its root.
        services.Settings.Set(SettingsCatalog.ToolchainRtlDir, @"H:\other\rtl");
        Assert.Equal([@"H:\other\rtl", @"G:\kit\ui", @"G:\kit\data"], library.Roots);
        Assert.False(library.IsLibrarySource(@"G:\rtl\System.pas"));
        Assert.True(library.IsLibrarySource(@"H:\other\rtl\System.pas"));
        services.Settings.Set(SettingsCatalog.ToolchainFrameworkDir, "");
        services.Settings.Set(SettingsCatalog.ToolchainDataDesignDir, "");
        Assert.Equal([@"H:\other\rtl"], library.Roots);

        // The same directory twice is one root.
        services.Settings.Set(SettingsCatalog.ToolchainFrameworkDir, @"h:\OTHER\rtl");
        Assert.Single(library.Roots);
        return Task.CompletedTask;
    });

    /// <summary>
    /// I-259: the Win32 binding units live beside the run-time library (<c>&lt;RtlDir&gt;/../winapi/src</c>); when that
    /// directory exists it is a root too, so the binding units open protected and count as library code for stepping.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-LIB-001")]
    public Task RootsIncludeWinApiBindings() => TestUiThread.RunAsync(() =>
    {
        var fs = new InMemoryFileSystem();
        var services = TestComposition.Build(o => o.FileSystem = fs);
        var library = services.LibrarySource;
        services.Settings.Set(SettingsCatalog.ToolchainRtlDir, @"G:\kit\rtl\src\");
        services.Settings.Set(SettingsCatalog.ToolchainFrameworkDir, @"G:\kit\ui");

        // Without the directory: the roots of the settings only.
        Assert.Equal([@"G:\kit\rtl\src", @"G:\kit\ui"], library.Roots);
        Assert.False(library.IsLibrarySource(@"G:\kit\rtl\winapi\src\Windows.pas"));

        // With it: the binding directory follows the other roots, normalised; its units are library source.
        fs.CreateDirectory(@"G:\kit\rtl\winapi\src");
        Assert.Equal([@"G:\kit\rtl\src", @"G:\kit\ui", @"G:\kit\rtl\winapi\src"], library.Roots);
        Assert.True(library.IsLibrarySource(@"G:\kit\rtl\winapi\src\Windows.pas"));
        Assert.True(library.IsLibrarySource(@"g:/kit/rtl/WINAPI/src/Messages.pas"));
        Assert.False(library.IsLibrarySource(@"G:\kit\rtl\winapi\def\kernel32.def"));
        Assert.False(library.IsLibrarySource(@"G:\kit\rtl\tests\T.pas"));

        // It follows the run-time library setting; without that setting there is no binding root.
        fs.CreateDirectory(@"H:\other\winapi\src");
        services.Settings.Set(SettingsCatalog.ToolchainRtlDir, @"H:\other\rtl");
        Assert.Equal([@"H:\other\rtl", @"G:\kit\ui", @"H:\other\winapi\src"], library.Roots);
        services.Settings.Set(SettingsCatalog.ToolchainRtlDir, "");
        Assert.Equal([@"G:\kit\ui"], library.Roots);
        return Task.CompletedTask;
    });
}
