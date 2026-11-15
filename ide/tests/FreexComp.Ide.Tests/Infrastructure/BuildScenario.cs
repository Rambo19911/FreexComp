using FreexComp.Ide.Core.ViewModels;

namespace FreexComp.Ide.Tests.Infrastructure;

/// <summary>
/// Phase 3 headless set-up: the Phase 3 tool windows, a valid (empty) toolchain under the temp root and the project
/// <c>Hello\Hello.pproj</c> (main source Hello.pas, member Unit1.pas) opened, with the fake engine and launcher.
/// </summary>
public static class BuildScenario
{
    public const string UnitText = "unit Unit1;\r\ninterface\r\nimplementation\r\nvar Foo1: Integer;\r\nbegin\r\n  Foo := 1;\r\nend.\r\n";

    public static async Task<HeadlessHarness> StartAsync(Action<HarnessOptions>? configure = null, string projectExtra = "")
    {
        var root = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));
        var work = Path.Combine(root, "work");
        var h = await HeadlessHarness.StartAsync(o =>
        {
            o.Root = root;
            o.ImplementedPhase = 3;
            foreach (var tool in (string[])[@"tc\llvm\clang.exe", @"tc\llvm\lld-link.exe", @"tc\llvm\llvm-dlltool.exe", @"tc\rtl\System.pas"])
                o.InitialFiles[tool] = "";
            o.Settings["toolchain.llvmBinDir"] = Path.Combine(work, @"tc\llvm");
            o.Settings["toolchain.rtlDir"] = Path.Combine(work, @"tc\rtl");
            o.InitialFiles[@"Hello\Hello.pproj"] = $$"""
                { "formatVersion": 1, "name": "Hello", "appType": "console", "mainSource": "Hello.pas",
                  "configurations": { "Debug": { "optimization": 0, "debugInfo": true, "defines": [], "outputDir": "bin/Debug" },
                                      "Release": { "optimization": 2, "debugInfo": false, "defines": [], "outputDir": "bin/Release" } },
                  "files": [ { "path": "Hello.pas" }, { "path": "Unit1.pas" } ]{{projectExtra}} }
                """;
            o.InitialFiles[@"Hello\Hello.pas"] = "program Hello;\r\nuses Unit1;\r\nbegin\r\nend.\r\n";
            o.InitialFiles[@"Hello\Unit1.pas"] = UnitText;
            o.InitialFiles[@"Hello\bin\Debug\.keep"] = "";
            configure?.Invoke(o);
        });
        h.Build.DefaultOutputPath = ExePath(h);
        if (!await h.Services.Projects.OpenAsync(ProjectPath(h)))
            throw new HarnessAssertionException("Hello.pproj did not open");
        return h;
    }

    public static string ProjectPath(HeadlessHarness h) => Path.Combine(h.TempDir, @"Hello\Hello.pproj");

    public static string UnitPath(HeadlessHarness h) => Path.Combine(h.TempDir, @"Hello\Unit1.pas");

    public static string ExePath(HeadlessHarness h) => Path.Combine(h.TempDir, @"Hello\bin\Debug\Hello.exe");

    public static MessagesViewModel Messages(HeadlessHarness h) =>
        (MessagesViewModel)h.Services.ToolWindows.Get(MessagesViewModel.ToolName)!;
}
