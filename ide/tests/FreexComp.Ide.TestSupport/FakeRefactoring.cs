using FreexComp.Ide.Core.Refactoring;

namespace FreexComp.Ide.TestSupport;

/// <summary>
/// A scripted refactoring (IDE-REFAC-030): whether it is applicable and what it computes are set by the test; the
/// contexts it was asked with are recorded.
/// </summary>
public sealed class FakeRefactoring(string id, string title, string? commandId = null) : IRefactoring
{
    public string Id => id;

    public string Title => title;

    public string? CommandId => commandId;

    public Func<RefactoringContext, bool> Applicable { get; set; } = _ => true;

    /// <summary>The result of <see cref="ComputeAsync"/>; by default null (the user cancelled).</summary>
    public Func<RefactoringContext, CancellationToken, Task<WorkspaceEdit?>> Compute { get; set; } = (_, _) => Task.FromResult<WorkspaceEdit?>(null);

    /// <summary>The context of the last <see cref="IsApplicable"/> call.</summary>
    public RefactoringContext? AskedWith { get; private set; }

    /// <summary>The contexts of the <see cref="ComputeAsync"/> calls, in order.</summary>
    public List<RefactoringContext> Computed { get; } = [];

    public bool IsApplicable(RefactoringContext context)
    {
        AskedWith = context;
        return Applicable(context);
    }

    public Task<WorkspaceEdit?> ComputeAsync(RefactoringContext context, CancellationToken ct)
    {
        Computed.Add(context);
        return Compute(context, ct);
    }
}

/// <summary>A scripted quick-action provider (IDE-REFAC-032); the contexts it was asked with are recorded.</summary>
public sealed class FakeQuickActionProvider(Func<RefactoringContext, CancellationToken, Task<IReadOnlyList<QuickAction>>> actions) : IQuickActionProvider
{
    public FakeQuickActionProvider(params QuickAction[] actions)
        : this((_, _) => Task.FromResult<IReadOnlyList<QuickAction>>(actions))
    {
    }

    public List<RefactoringContext> Asked { get; } = [];

    public Task<IReadOnlyList<QuickAction>> GetActionsAsync(RefactoringContext context, CancellationToken ct)
    {
        Asked.Add(context);
        return actions(context, ct);
    }
}
