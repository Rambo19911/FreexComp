using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using FreexComp.Build;
using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Core.Dialogs;

/// <summary>
/// The Options page <c>Environment.Trust</c> "Trusted Locations" (dialogs.md §9.10, UI-DLG-297; IDE-TRUST-014…017): the
/// entries of the trust list as read when the dialog opened, minus the ones removed on the page. A custom page: it is
/// not bound to settings keys, and it cannot add an entry — trust is given where the commands are shown.
/// </summary>
public sealed class TrustedLocationsPageViewModel : ObservableObject
{
    private readonly ITrustStore? _store;
    private readonly List<TrustEntry> _removed = [];
    private IReadOnlyList<TrustEntry> _entries;

    /// <param name="store">The trust list; null = a page without a list (always empty).</param>
    public TrustedLocationsPageViewModel(ITrustStore? store)
    {
        _store = store;
        var snapshot = store?.Read();
        _entries = snapshot is null || snapshot.Entries.IsDefault ? [] : [.. snapshot.Entries]; // folders, then projects (IDE-TRUST-015)
        Warning = snapshot?.ReadProblem is { } problem
            ? string.Format(CultureInfo.CurrentCulture, Strings.Msg_Options_TrustUnreadable, problem)
            : null;
    }

    /// <summary>The labels the Options filter matches besides the page title (IDE-TRUST-018).</summary>
    internal static IReadOnlyList<string> FieldLabels => [Strings.Options_Trust_ListLabel, Strings.Options_Trust_Remove, Strings.Options_Trust_RemoveAll];

    public string Hint => Strings.Msg_Options_TrustHint;

    public string EmptyText => Strings.Msg_Options_TrustEmpty;

    /// <summary><c>Msg.Options.TrustUnreadable</c> with the reason when the list could not be read; else null.</summary>
    public string? Warning { get; }

    /// <summary>The working copy: the entries still trusted after OK.</summary>
    public IReadOnlyList<TrustEntry> Entries
    {
        get => _entries;
        private set
        {
            if (SetProperty(ref _entries, value))
                OnPropertyChanged(nameof(CanRemoveAll));
        }
    }

    public bool CanRemoveAll => Entries.Count > 0;

    /// <summary>True while entries were removed on the page and not written yet.</summary>
    public bool HasChanges => _removed.Count > 0;

    /// <summary>The working copy changed.</summary>
    public event EventHandler? PendingChanged;

    public static string KindText(TrustEntryKind kind) =>
        kind == TrustEntryKind.Folder ? Strings.Options_Trust_Kind_Folder : Strings.Options_Trust_Kind_Project;

    /// <summary>IDE-TRUST-016 <c>Remove</c> / the Delete key: the selected entries leave the working copy. Nothing is asked.</summary>
    public void Remove(IReadOnlyList<TrustEntry> selected)
    {
        ArgumentNullException.ThrowIfNull(selected);
        var leaving = Entries.Where(selected.Contains).ToList();
        if (leaving.Count == 0)
            return;
        _removed.AddRange(leaving);
        Entries = [.. Entries.Except(leaving)];
        PendingChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>IDE-TRUST-016 <c>Remove All</c>.</summary>
    public void RemoveAll() => Remove(Entries);

    /// <summary>
    /// IDE-TRUST-017 OK / Apply: <c>Store.Remove</c> for each removed entry; entries another instance added meanwhile are
    /// untouched. An entry that could not be removed stays pending.
    /// </summary>
    /// <exception cref="IOException">The trust list could not be written.</exception>
    internal void Apply()
    {
        while (_removed.Count > 0)
        {
            _store?.Remove(_removed[0]);
            _removed.RemoveAt(0);
        }
    }
}
