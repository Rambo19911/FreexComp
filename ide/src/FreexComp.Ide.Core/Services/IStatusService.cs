namespace FreexComp.Ide.Core.Services;

/// <summary>Status bar (architecture.md §4). Only transient messages so far; state and progress come with their tasks.</summary>
public interface IStatusService
{
    /// <summary>Shows <paramref name="text"/> in the message field for <paramref name="duration"/> (UI-MSG-003 "Status").</summary>
    void ShowMessage(string text, TimeSpan duration);
}
