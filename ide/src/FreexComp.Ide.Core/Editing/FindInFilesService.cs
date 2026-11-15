using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Projects;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;

namespace FreexComp.Ide.Core.Editing;

/// <summary>Where Find in Files looks (IDE-ED-120).</summary>
public enum FindInFilesScope
{
    Project,
    OpenFiles,
    Folder,
}

/// <summary>
/// One Find in Files run (dialogs.md §11). <paramref name="Folder"/>, <paramref name="Subfolders"/> and
/// <paramref name="FileMasks"/> (<c>;</c>-separated wildcards) apply to <see cref="FindInFilesScope.Folder"/> only.
/// </summary>
public sealed record FindInFilesRequest(FindQuery Query, FindInFilesScope Scope, string? Folder = null, bool Subfolders = true,
    string FileMasks = FindInFilesRequest.DefaultMasks)
{
    public const string DefaultMasks = "*.pas;*.inc;*.form";
}

/// <summary>A match: absolute path, 1-based line and column, length, and the text of its line (without the line break).</summary>
public sealed record FileMatch(string Path, int Line, int Column, int Length, string LineText);

/// <summary>
/// The outcome of a run. <see cref="FileCount"/> = files with at least one match. When canceled, <see cref="Matches"/> holds
/// what was already reported. <see cref="Error"/> = the regular expression is invalid (nothing was searched).
/// </summary>
public sealed record FindInFilesResult(IReadOnlyList<FileMatch> Matches, int FileCount, bool IsCapped, bool IsCanceled, string? Error);

/// <summary>
/// Find in Files (editor-features.md §12): open documents are searched in their buffers (IDE-ED-121), other files read
/// from disk with the open-file encoding detection; matching is the in-editor find, line by line (IDE-ED-122). Files are
/// searched on up to min(4, processors) background threads; results are reported in file order (IDE-ED-124) in batches
/// every 100 ms of <see cref="IClock"/> time (and at the end), capped at <see cref="MatchCap"/> (IDE-ED-123). Start it on
/// the UI thread (the buffers are read there); batches arrive from the clock callback.
/// </summary>
public sealed class FindInFilesService(IDocumentService documents, IProjectService projects, IFileSystem fileSystem,
    ISettingsService settings, IClock clock)
{
    /// <summary>IDE-ED-123: the search stops at this many matches.</summary>
    public const int MatchCap = 5000;

    private static readonly TimeSpan BatchInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>IDE-ED-120: folders never searched in folder scope.</summary>
    private static readonly HashSet<string> SkippedFolders = new(StringComparer.OrdinalIgnoreCase) { ".freexcomp", "obj", "bin", ".git" };

    public async Task<FindInFilesResult> SearchAsync(FindInFilesRequest request, IProgress<IReadOnlyList<FileMatch>>? batches,
        CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (cancellation.IsCancellationRequested)
            return new FindInFilesResult([], 0, false, true, null);
        if (request.Query.Regex)
        {
            try
            {
                TextFinder.CreateRegex(request.Query);
            }
            catch (ArgumentException e)
            {
                return new FindInFilesResult([], 0, false, false, e.Message);
            }
        }

        // UI thread: the file list and the buffer snapshots (IDE-ED-121).
        var buffers = documents.Documents.OfType<TextDocumentViewModel>()
            .Where(d => d.FilePath is not null)
            .GroupBy(d => d.FilePath!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Buffer.Text, StringComparer.OrdinalIgnoreCase);
        var files = Files(request, buffers);
        var limit = (long)settings.Get(SettingsCatalog.FilesLargeFileThresholdMb) * 1024 * 1024;

        var collector = new Collector(files.Count, batches);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        var timer = clock.Schedule(BatchInterval, Tick);
        try
        {
            await Parallel.ForEachAsync(Enumerable.Range(0, files.Count),
                new ParallelOptions { MaxDegreeOfParallelism = Math.Min(4, Environment.ProcessorCount), CancellationToken = linked.Token },
                (index, ct) =>
                {
                    var path = files[index];
                    var text = buffers.TryGetValue(path, out var buffer) ? buffer : Read(path, limit);
                    var matches = text is null ? [] : Search(path, text, request.Query, ct);
                    if (collector.Add(index, matches))
                        linked.Cancel(); // the ordered prefix reached the cap
                    return ValueTask.CompletedTask;
                }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Canceled by the caller, or stopped at the cap.
        }
        finally
        {
            lock (collector)
                timer.Dispose();
        }

        var canceled = cancellation.IsCancellationRequested;
        var reported = canceled ? collector.Reported() : collector.Flush(final: true);
        return new FindInFilesResult(reported, reported.Select(m => m.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            collector.IsCapped && !canceled, canceled, null);

        void Tick()
        {
            if (cancellation.IsCancellationRequested)
                return;
            collector.Flush(final: false);
            lock (collector)
            {
                if (!collector.IsDone)
                    timer = clock.Schedule(BatchInterval, Tick);
            }
        }
    }

    /// <summary>IDE-ED-120/124: the files of the scope, in result order.</summary>
    private List<string> Files(FindInFilesRequest request, IReadOnlyDictionary<string, string> buffers)
    {
        switch (request.Scope)
        {
            case FindInFilesScope.OpenFiles:
                return [.. buffers.Keys];
            case FindInFilesScope.Project:
                if (projects.Project is not { } project)
                    return [];
                var paths = new[] { project.MainSource }.Concat(project.Files.Select(f => f.Path))
                    .Where(p => p.Length > 0)
                    .Select(p => Path.GetFullPath(Path.Combine(project.Directory, p.Replace('/', '\\'))))
                    .Distinct(StringComparer.OrdinalIgnoreCase);
                return [.. paths.Where(p => buffers.ContainsKey(p) || fileSystem.FileExists(p))];
            default:
                if (request.Folder is not { Length: > 0 } folder || !fileSystem.DirectoryExists(folder))
                    return [];
                var masks = request.FileMasks.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                Collect(folder, masks.Length == 0 ? ["*"] : masks, request.Subfolders, found);
                return [.. found.Order(StringComparer.OrdinalIgnoreCase)];
        }
    }

    private void Collect(string folder, IReadOnlyList<string> masks, bool recursive, HashSet<string> found)
    {
        foreach (var mask in masks)
        {
            foreach (var file in fileSystem.EnumerateFiles(folder, mask))
                found.Add(file);
        }

        if (!recursive)
            return;
        foreach (var sub in fileSystem.EnumerateDirectories(folder))
        {
            if (!SkippedFolders.Contains(Path.GetFileName(sub)))
                Collect(sub, masks, recursive, found);
        }
    }

    /// <summary>A file from disk, or null when it is too large, binary or unreadable (IDE-ED-120).</summary>
    private string? Read(string path, long limit)
    {
        try
        {
            if (fileSystem.GetLength(path) > limit)
                return null;
            var detection = EncodingDetector.Detect(fileSystem.ReadAllBytes(path));
            return detection.IsBinary ? null : detection.Text;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>IDE-ED-122: the in-editor matching, one line at a time.</summary>
    private static List<FileMatch> Search(string path, string text, FindQuery query, CancellationToken cancellation)
    {
        var matches = new List<FileMatch>();
        var lineNumber = 0;
        var start = 0;
        while (start <= text.Length && matches.Count < MatchCap)
        {
            cancellation.ThrowIfCancellationRequested();
            lineNumber++;
            var end = text.IndexOfAny(['\r', '\n'], start);
            var lineEnd = end < 0 ? text.Length : end;
            var line = text[start..lineEnd];
            foreach (var m in TextFinder.FindAll(line, query, 0, line.Length, cancellation, MatchCap - matches.Count).Matches)
                matches.Add(new FileMatch(path, lineNumber, m.Offset + 1, m.Length, line));
            if (end < 0)
                break;
            start = end + (text[end] == '\r' && end + 1 < text.Length && text[end + 1] == '\n' ? 2 : 1);
            if (start == text.Length)
                break;
        }

        return matches;
    }

    /// <summary>Per-file results; the contiguous finished prefix is reported in order (IDE-ED-124).</summary>
    private sealed class Collector(int count, IProgress<IReadOnlyList<FileMatch>>? progress)
    {
        private readonly List<FileMatch>?[] _results = new List<FileMatch>?[count];
        private readonly List<FileMatch> _reported = [];
        private int _next;
        private int _ready; // matches in the finished prefix

        public bool IsCapped { get; private set; }

        public bool IsDone => _next >= count || IsCapped;

        /// <summary>True when the finished prefix reached the cap (the rest need not run).</summary>
        public bool Add(int index, List<FileMatch> matches)
        {
            lock (this)
            {
                _results[index] = matches;
                var ready = 0;
                for (var i = 0; i < count && _results[i] is { } r; i++)
                    ready += r.Count;
                _ready = ready;
                return _ready >= MatchCap;
            }
        }

        public IReadOnlyList<FileMatch> Reported()
        {
            lock (this)
                return [.. _reported];
        }

        /// <summary>Reports the newly finished prefix; with <paramref name="final"/> everything (all files finished or capped).</summary>
        public IReadOnlyList<FileMatch> Flush(bool final)
        {
            List<FileMatch> batch = [];
            lock (this)
            {
                while (_next < count && _results[_next] is { } r && _reported.Count + batch.Count < MatchCap)
                {
                    batch.AddRange(r.Take(MatchCap - _reported.Count - batch.Count));
                    _next++;
                }

                if (_reported.Count + batch.Count >= MatchCap && (_next < count || _ready > MatchCap))
                    IsCapped = true;
                _reported.AddRange(batch);
                if (final)
                    _next = count;
            }

            if (batch.Count > 0)
                progress?.Report(batch);
            return Reported();
        }
    }
}
