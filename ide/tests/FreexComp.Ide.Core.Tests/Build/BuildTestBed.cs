using System.Text;
using FreexComp.Ide.Core.Composition;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Build;

/// <summary>
/// A test composition for build and run (phase3-build-run.md): in-memory files, a valid toolchain under <c>G:\</c>, the
/// fake engine and launcher, the <c>Hello</c> project at <see cref="Dir"/> (main source Hello.pas, member Unit1.pas).
/// </summary>
internal sealed class BuildTestBed
{
    public const string Dir = @"C:\p\Hello";
    public const string ProjectPath = Dir + @"\Hello.pproj";
    public const string MainPath = Dir + @"\Hello.pas";
    public const string UnitPath = Dir + @"\Unit1.pas";

    public BuildTestBed(Action<TestCompositionOptions>? configure = null)
    {
        Services = TestComposition.Build(o =>
        {
            o.FileSystem = Fs;
            o.Clock = Clock;
            o.Log = Log;
            o.BuildEngine = Engine;
            o.Processes = Processes;
            configure?.Invoke(o);
        });
        foreach (var tool in (string[])[@"G:\llvm\clang.exe", @"G:\llvm\lld-link.exe", @"G:\llvm\llvm-dlltool.exe", @"G:\rtl\System.pas"])
            File(tool, "");
        Services.Settings.Set(SettingsCatalog.ToolchainLlvmBinDir, @"G:\llvm");
        Services.Settings.Set(SettingsCatalog.ToolchainRtlDir, @"G:\rtl");
        Services.Settings.Set(SettingsCatalog.RunConsoleMode, "output"); // the harness preset (testing.md §2.1)
    }

    public InMemoryFileSystem Fs { get; } = new();

    public ManualClock Clock { get; } = new();

    public RecordingLog Log { get; } = new();

    public FakeBuildEngine Engine { get; } = new();

    public FakeProcessLauncher Processes { get; } = new();

    public IdeServices Services { get; }

    public FakeDialogService Dialogs => (FakeDialogService)Services.Dialogs;

    public string File(string path, string text)
    {
        Fs.CreateDirectory(Path.GetDirectoryName(path)!);
        Fs.WriteAllBytes(path, Encoding.UTF8.GetBytes(text));
        return path;
    }

    public string Text(string path) => Encoding.UTF8.GetString(Fs.ReadAllBytes(path));

    /// <summary>Writes and opens Hello.pproj; <paramref name="extra"/> is added to the JSON object.</summary>
    public async Task OpenHelloAsync(string appType = "console", string extra = "")
    {
        File(ProjectPath, $$"""
            { "formatVersion": 1, "name": "Hello", "appType": "{{appType}}", "mainSource": "Hello.pas",
              "files": [ { "path": "Hello.pas" }, { "path": "Unit1.pas" } ]{{extra}} }
            """);
        File(MainPath, "program Hello;\nuses Unit1;\nbegin\nend.\n");
        File(UnitPath, "unit Unit1;\ninterface\nimplementation\nend.\n");
        Assert.True(await Services.Projects.OpenAsync(ProjectPath));
    }

    public async Task<TextDocumentViewModel> OpenDocumentAsync(string path)
    {
        var result = await Services.Documents.OpenAsync(path);
        Assert.True(result.Success, result.MessageId);
        return Assert.IsType<TextDocumentViewModel>(result.Value);
    }

    public static void Type(TextDocumentViewModel document, string text, int offset = 0) =>
        document.Buffer.ApplyEdits([new TextEdit(offset, 0, text)]);

    /// <summary>Lets batched Output reach its channels (50 ms, IDE-P3-070).</summary>
    public void FlushOutput() => Clock.Advance(TimeSpan.FromMilliseconds(50));

    public IReadOnlyList<string> OutputText(string channel)
    {
        FlushOutput();
        return [.. Services.Output.GetLines(channel).Select(l => l.Text)];
    }
}
