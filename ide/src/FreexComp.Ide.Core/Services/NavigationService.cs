using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;

namespace FreexComp.Ide.Core.Services;

/// <summary>A navigation moved the caret of <see cref="Document"/> away from <see cref="From"/> (the history entry, IDE-ED-100).</summary>
public sealed class NavigatedEventArgs(TextDocumentViewModel document, TextPosition from) : EventArgs
{
    public TextDocumentViewModel Document { get; } = document;

    public TextPosition From { get; } = from;
}

/// <summary>
/// Caret navigation inside documents (commands.md: owner of <c>search.goToLine</c>). The navigation history
/// (<c>search.navigateBack/Forward</c>, IDE-ED-100) builds on <see cref="Navigated"/> (task I-060). UI thread only.
/// </summary>
public interface INavigationService
{
    /// <summary>Raised after a navigation with the position it left.</summary>
    event EventHandler<NavigatedEventArgs>? Navigated;

    /// <summary>
    /// UI-DLG-120 / phase1-shell.md §10: caret to <paramref name="line"/> (clamped to the document), column
    /// <paramref name="column"/> (below 1 → 1, beyond the line → its end; null → the first non-blank character), selection
    /// cleared, folds hiding the line opened, the line scrolled to one third of the viewport.
    /// </summary>
    void GoToLine(TextDocumentViewModel document, int line, int? column);
}

/// <inheritdoc cref="INavigationService"/>
public sealed class NavigationService(IDocumentService documents, IDialogService dialogs) : INavigationService
{
    public event EventHandler<NavigatedEventArgs>? Navigated;

    public void GoToLine(TextDocumentViewModel document, int line, int? column)
    {
        ArgumentNullException.ThrowIfNull(document);
        var buffer = document.Buffer;
        var from = document.ViewState.Caret;
        line = Math.Clamp(line, 1, buffer.LineCount);
        var (start, length, _) = buffer.GetLine(line);
        var offset = column is { } c ? start + Math.Clamp(c, 1, length + 1) - 1 : start + FirstNonBlank(buffer.GetText(start, length));
        document.Folding.Reveal(start);
        document.Folding.Reveal(offset);
        document.Selection = TextSelection.At(offset);
        document.RevealCaret(third: true);
        Navigated?.Invoke(this, new NavigatedEventArgs(document, from));
    }

    public void RegisterCommands(ICommandService commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        commands.RegisterHandler("search.goToLine", new Handler(this)); // menus.md: "text"
    }

    /// <summary>Index of the first character that is not a space or tab; 0 for a blank line.</summary>
    private static int FirstNonBlank(string text)
    {
        var index = 0;
        while (index < text.Length && text[index] is ' ' or '\t')
            index++;
        return index == text.Length ? 0 : index;
    }

    private sealed class Handler(NavigationService owner) : ICommandHandler
    {
        public bool CanExecute(string? argument) => owner.Active is not null;

        public async Task ExecuteAsync(string? argument)
        {
            if (owner.Active is not { } document)
                return;
            var dialog = new GoToLineViewModel(document.Buffer.LineCount, document.ViewState.Caret.Line);
            if (await owner._dialogs.ShowAsync(dialog) is { } target && owner._documents.Documents.Contains(document))
                owner.GoToLine(document, target.Line, target.Column);
        }
    }

    private readonly IDialogService _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));

    private readonly IDocumentService _documents = documents ?? throw new ArgumentNullException(nameof(documents));

    private TextDocumentViewModel? Active => _documents.ActiveDocument as TextDocumentViewModel;
}
