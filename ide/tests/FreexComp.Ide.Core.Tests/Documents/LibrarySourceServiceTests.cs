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
}
