using System.Collections.Immutable;
using System.Globalization;
using FreexComp.Build;
using FreexComp.Compiler;
using FreexComp.Compiler.Text;
using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Build;

public sealed partial class BuildServiceTests
{
    private static string F(string format, params object[] args) => string.Format(CultureInfo.CurrentCulture, format, args);

    private static Diagnostic Error(string code, string message, string path, int line, int column, int length = 0) =>
        FakeBuildEngine.Diag(code, DiagnosticSeverity.Error, message, path, line, column, length);

    /// <summary>IDE-P3-061: the same diagnostic twice in one build (a unit compiled twice) is one row.</summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P3-061")]
    [Trait("Req", "IDE-P3-063")]
    public async Task DuplicateDiagnosticsAreDropped()
    {
        var bed = new BuildTestBed();
        await bed.OpenHelloAsync();
        var foo = Error("E2003", "Undeclared identifier 'Foo'", BuildTestBed.UnitPath, 12, 5, 3);
        var otherColumn = Error("E2003", "Undeclared identifier 'Foo'", BuildTestBed.UnitPath, 12, 9, 3);
        var warning = FakeBuildEngine.Diag("W1002", DiagnosticSeverity.Warning, "Variable 'I' is declared but never used", BuildTestBed.UnitPath, 3, 3);
        var hint = FakeBuildEngine.Diag("H2077", DiagnosticSeverity.Hint, "Value assigned to 'X' never used", BuildTestBed.UnitPath, 4, 3);
        bed.Engine.Enqueue(FakeBuildEngine.Failure(BuildTestBed.ProjectPath, foo, foo with { }, otherColumn, warning, warning, hint));

        await bed.Services.Build.BuildAsync(BuildKind.Build);

        var messages = bed.Services.Messages;
        Assert.Equal(["E2003", "E2003", "W1002", "H2077", ""], messages.Items.Select(i => i.Code));
        Assert.Equal([0, 1, 2, 3, 4], messages.Items.Select(i => i.Index));
        Assert.Equal((2, 1, 1), (messages.ErrorCount, messages.WarningCount, messages.HintCount));
        Assert.True(messages.Items[^1].IsSummary);

        // The next build starts from an empty list: the same diagnostic is listed again.
        bed.Engine.Enqueue(FakeBuildEngine.Failure(BuildTestBed.ProjectPath, foo));
        await bed.Services.Build.BuildAsync(BuildKind.Build);
        Assert.Equal(["E2003", ""], messages.Items.Select(i => i.Code));
        Assert.Equal((1, 0, 0), (messages.ErrorCount, messages.WarningCount, messages.HintCount));
    }

    /// <summary>IDE-P3-062: a tool failure is one E40xx row without position; the raw tool lines go to Output only.</summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P3-062")]
    public async Task ToolErrorLinesBecomeRows()
    {
        var bed = new BuildTestBed();
        await bed.OpenHelloAsync();
        var linkError = FakeBuildEngine.Diag("E4005", DiagnosticSeverity.Error, "lld-link failed with exit code 1", BuildTestBed.ProjectPath);
        var now = DateTime.UtcNow;
        bed.Engine.Enqueue(new FakeBuild(
            [
                new BuildEvent.OutputLine(now, BuildTestBed.ProjectPath, OutputSource.Tool, "lld-link: error: undefined symbol: Foo"),
                new BuildEvent.OutputLine(now, BuildTestBed.ProjectPath, OutputSource.Tool, @"C:\p\Hello\Unit1.pas(3,1): error E9999: not a row"),
                new BuildEvent.Diagnostic(now, BuildTestBed.ProjectPath, linkError),
            ],
            FakeBuildEngine.Result(BuildTestBed.ProjectPath, success: false, errors: 1)));

        await bed.Services.Build.BuildAsync(BuildKind.Build);

        var row = Assert.Single(bed.Services.Messages.Items, i => !i.IsSummary);
        Assert.Equal(("E4005", MessageSeverity.Error), (row.Code, row.Severity));
        Assert.Null(row.Location);
        Assert.False(row.IsNavigable);
        var output = bed.OutputText(OutputChannels.Build);
        Assert.Contains("lld-link: error: undefined symbol: Foo", output);
        Assert.Contains(@"C:\p\Hello\Unit1.pas(3,1): error E9999: not a row", output);
    }

    /// <summary>IDE-P3-030, -060, -071: the request, the rows (mapping) and the build channel in order.</summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P3-030")]
    [Trait("Req", "IDE-P3-060")]
    [Trait("Req", "IDE-P3-071")]
    [Trait("Req", "IDE-P3-013")]
    public async Task EventsBecomeMessagesAndOutput()
    {
        var bed = new BuildTestBed();
        await bed.OpenHelloAsync();
        bed.Services.Settings.Set(SettingsCatalog.BuildShowCommandLines, true);
        bed.Services.Output.Append(OutputChannels.Build, "old line", OutputKind.Normal);
        bed.FlushOutput();

        var related = new RelatedLocation(new Location(BuildTestBed.UnitPath, default,
            new LinePositionSpan(new LinePosition(2, 0), new LinePosition(2, 0))), "Declared here");
        var foo = Error("E2003", "Undeclared identifier 'Foo'", BuildTestBed.UnitPath, 12, 5, 3) with { Related = [related] };
        var now = DateTime.UtcNow;
        bed.Engine.Enqueue(new FakeBuild(
            [
                new BuildEvent.CommandLine(now, BuildTestBed.ProjectPath, "clang.exe -c Hello.ll"),
                new BuildEvent.Diagnostic(now, BuildTestBed.ProjectPath, foo),
                new BuildEvent.OutputLine(now, BuildTestBed.ProjectPath, OutputSource.Info, "Compiling Unit1"),
            ],
            FakeBuildEngine.Result(BuildTestBed.ProjectPath, success: false, errors: 1)));
        var states = new List<IdeState>();
        bed.Services.State.Changed += (_, _) => states.Add(bed.Services.State.Current.State);

        var result = await bed.Services.Build.BuildAsync(BuildKind.Build);

        Assert.NotNull(result);
        Assert.Equal([IdeState.Building, IdeState.Idle], states);
        var request = Assert.Single(bed.Engine.Requests);
        Assert.Equal((BuildTestBed.ProjectPath, "Debug", "win64", BuildTarget.Build, true),
            (request.ProjectPath, request.Configuration, request.Platform, request.Target, request.Verbose));
        Assert.Equal((@"G:\llvm", @"G:\rtl"), (request.ToolchainDirectory, request.RtlDirectory));
        Assert.Empty(request.OpenFiles);

        var row = bed.Services.Messages.Items[0];
        Assert.Equal(new MessageLocation(BuildTestBed.UnitPath, 12, 5, 12, 8), row.Location);
        Assert.Equal(("E2003", "Undeclared identifier 'Foo'", MessageSeverity.Error), (row.Code, row.Text, row.Severity));
        var child = Assert.Single(row.Related);
        Assert.Equal((MessageSeverity.Info, "Declared here"), (child.Severity, child.Text));
        Assert.Equal(new MessageLocation(BuildTestBed.UnitPath, 3, 1, 3, 1), child.Location); // empty span: end = start
        Assert.Equal(Strings.Msg_Build_FailedOne, bed.Services.Messages.Items[^1].Text);

        // output.clearOnBuild (default true) emptied the channel first.
        Assert.Equal(
            [
                F(Strings.Msg_Build_StartedHeader, "Hello", "Debug", "win64"),
                F(Strings.Msg_Build_CommandLine, "clang.exe -c Hello.ll"),
                foo.ToText(),
                "Compiling Unit1",
                Strings.Msg_Build_FailedOne,
            ],
            bed.OutputText(OutputChannels.Build));
        var kinds = bed.Services.Output.GetLines(OutputChannels.Build).Select(l => l.Kind);
        Assert.Equal([OutputKind.Ide, OutputKind.Command, OutputKind.Error, OutputKind.Ide, OutputKind.Failure], kinds);

        // Rebuild and Clean map to their targets; Clean does not save.
        await bed.Services.Build.BuildAsync(BuildKind.Rebuild);
        await bed.Services.Build.BuildAsync(BuildKind.Clean);
        Assert.Equal([BuildTarget.Build, BuildTarget.Rebuild, BuildTarget.Clean], bed.Engine.Requests.Select(r => r.Target));
    }

    /// <summary>IDE-P3-034: the summary text for each kind of result (state field, Output, Messages summary row).</summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P3-034")]
    public async Task SummaryTextsPerResult()
    {
        var bed = new BuildTestBed();
        await bed.OpenHelloAsync();
        var path = BuildTestBed.ProjectPath;
        var elapsed = TimeSpan.FromMilliseconds(2345);
        var seconds = 2.3.ToString("0.0", CultureInfo.CurrentCulture);
        var cases = new (BuildKind Kind, BuildResult Result, string Expected)[]
        {
            (BuildKind.Build, FakeBuildEngine.Result(path, true, @"C:\p\Hello\Hello.exe", elapsed: elapsed), F(Strings.Msg_Build_Succeeded, seconds)),
            (BuildKind.Build, FakeBuildEngine.Result(path, true, "x.exe", warnings: 1, elapsed: elapsed), F(Strings.Msg_Build_SucceededWithWarningsOne, seconds)),
            (BuildKind.Build, FakeBuildEngine.Result(path, true, "x.exe", warnings: 3, elapsed: elapsed), F(Strings.Msg_Build_SucceededWithWarningsMany, seconds, 3)),
            (BuildKind.Build, FakeBuildEngine.Result(path, true, "x.exe", upToDate: true), F(Strings.Msg_Build_UpToDate, "Hello")),
            (BuildKind.Build, FakeBuildEngine.Result(path, false, errors: 1), Strings.Msg_Build_FailedOne),
            (BuildKind.Rebuild, FakeBuildEngine.Result(path, false, errors: 4), F(Strings.Msg_Build_FailedMany, 4)),
            (BuildKind.Build, FakeBuildEngine.Result(path, false, canceled: true, errors: 1), Strings.Msg_Build_Canceled),
            (BuildKind.Clean, FakeBuildEngine.Result(path, true), F(Strings.Msg_Build_Cleaned, "Hello")),
        };
        foreach (var (kind, result, expected) in cases)
        {
            bed.Engine.Enqueue(new FakeBuild([], result));
            await bed.Services.Build.BuildAsync(kind);
            Assert.Equal(expected, bed.Services.Build.ResultText);
            var summary = bed.Services.Messages.Items[^1];
            Assert.True(summary.IsSummary);
            Assert.Equal(expected, summary.Text);
            Assert.Equal(1, bed.OutputText(OutputChannels.Build).Count(l => l == expected)); // exactly once
        }

        // An E0001 diagnostic in the result is an internal error.
        var ice = FakeBuildEngine.Diag("E0001", DiagnosticSeverity.Error, "Internal compiler error: boom", BuildTestBed.MainPath, 1, 1);
        bed.Engine.Enqueue(FakeBuildEngine.Failure(path, ice));
        await bed.Services.Build.BuildAsync(BuildKind.Build);
        Assert.Equal(Strings.Msg_Build_InternalError, bed.Services.Build.ResultText);

        // The cancellation diagnostic E9010 is not a row.
        var canceled = FakeBuildEngine.Diag("E9010", DiagnosticSeverity.Error, "Build canceled", path);
        bed.Engine.Enqueue(new FakeBuild([new BuildEvent.Diagnostic(DateTime.UtcNow, path, canceled)],
            FakeBuildEngine.Result(path, false, canceled: true, errors: 1)));
        await bed.Services.Build.BuildAsync(BuildKind.Build);
        Assert.Equal([Strings.Msg_Build_Canceled], bed.Services.Messages.Items.Select(i => i.Text));
    }

    /// <summary>IDE-P3-037: an exception from the engine is an internal error; the state returns to Idle.</summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P3-037")]
    public async Task EngineExceptionReturnsToIdle()
    {
        var bed = new BuildTestBed();
        await bed.OpenHelloAsync();
        bed.Engine.Enqueue(new FakeBuild([], FakeBuildEngine.Result(BuildTestBed.ProjectPath, true)) { Throw = new InvalidOperationException("engine exploded") });

        var result = await bed.Services.Build.BuildAsync(BuildKind.Build);

        Assert.Null(result);
        Assert.Equal(IdeState.Idle, bed.Services.State.Current.State);
        Assert.Equal(Strings.Msg_Build_InternalError, bed.Services.Build.ResultText);
        var rows = bed.Services.Messages.Items;
        Assert.Equal((MessageSeverity.Error, Strings.Msg_Build_InternalError, false), (rows[0].Severity, rows[0].Text, rows[0].IsSummary));
        Assert.Null(rows[0].Location);
        Assert.Contains(bed.OutputText(OutputChannels.Build), l => l.Contains("engine exploded", StringComparison.Ordinal));
        Assert.Contains(bed.Log.Lines, l => l.StartsWith("ERROR build:", StringComparison.Ordinal));

        // The next build works.
        Assert.NotNull(await bed.Services.Build.BuildAsync(BuildKind.Build));
    }

    /// <summary>IDE-P3-012: no build, no save, a message with Open Options when the toolchain is missing.</summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P3-012")]
    public async Task ToolchainMissingDoesNotBuild()
    {
        var bed = new BuildTestBed();
        await bed.OpenHelloAsync();
        bed.Services.Settings.Set(SettingsCatalog.ToolchainLlvmBinDir, @"G:\gone");
        bed.Dialogs.Answer("Msg.Build.ToolchainMissing", "Cancel");

        Assert.Null(await bed.Services.Build.BuildAsync(BuildKind.Build));
        Assert.Empty(bed.Engine.Requests);
        Assert.Equal(IdeState.Idle, bed.Services.State.Current.State);
    }
}
