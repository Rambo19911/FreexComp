using System.Reflection;
using FreexComp.Debugger;
using FreexComp.Ide.Core.Debugging;
using FreexComp.Ide.Core.Dialogs;

namespace FreexComp.Ide.Core.Tests.Dialogs;

/// <summary>
/// Evaluate / Modify (dialogs.md §21) with two evaluations under way at once: the dialog evaluates the expression it
/// was opened with, the user types another and presses Evaluate before the first answer has come (I-258: on a busy
/// machine the first answer is late; it replaced the newer result, and the dialog showed the value of an expression
/// that was no longer in its box).
/// </summary>
[Trait("Phase", "6")]
public sealed class EvaluateOrderTests
{
    /// <summary>A debug service that is paused and answers each evaluation when the test says so.</summary>
    public class PendingEvaluations : DispatchProxy
    {
        public Dictionary<string, TaskCompletionSource<EvaluateResult>> Asked { get; } = new(StringComparer.Ordinal);

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod!.Name switch
        {
            "get_" + nameof(IDebugService.IsPaused) => true,
            nameof(IDebugService.EvaluateAsync) => (Asked[(string)args![0]!] = new TaskCompletionSource<EvaluateResult>()).Task,
            _ => throw new NotSupportedException(targetMethod.Name),
        };
    }

    private static EvaluateResult Value(string expression, string value) =>
        new(new VariableInfo(expression, value, "Integer", ValueKind.Integer, null, 0, false, null, null));

    [Fact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-210")]
    public async Task LateAnswerOfAnEarlierEvaluationDoesNotReplaceTheNewerResult()
    {
        var debug = DispatchProxy.Create<IDebugService, PendingEvaluations>();
        var asked = ((PendingEvaluations)(object)debug).Asked;

        // Opened with an expression: evaluated at once. Its answer is still on its way when the next one is asked for.
        var dialog = new EvaluateViewModel(debug, null, "First");
        var first = dialog.Evaluation;
        dialog.Expression = "Second";
        var second = dialog.EvaluateAsync();
        Assert.Equal(["First", "Second"], asked.Keys);

        asked["Second"].SetResult(Value("Second", "2"));
        await second;
        Assert.Equal(("2", false, "Integer"), (dialog.ResultText, dialog.ResultIsError, dialog.Type));

        // The late answer — a value or a failure — belongs to an expression the dialog no longer shows.
        asked["First"].SetResult(Value("First", "1"));
        await first;
        Assert.Equal(("2", false, "Integer"), (dialog.ResultText, dialog.ResultIsError, dialog.Type));

        dialog.Expression = "Third";
        var third = dialog.EvaluateAsync();
        dialog.Expression = "Fourth";
        var fourth = dialog.EvaluateAsync();
        asked["Fourth"].SetResult(Value("Fourth", "4"));
        await fourth;
        asked["Third"].SetException(new DebuggerException(DebugErrorCode.RequestFailed, "unknown identifier"));
        await third;
        Assert.Equal(("4", false, "Integer"), (dialog.ResultText, dialog.ResultIsError, dialog.Type));
    }
}
