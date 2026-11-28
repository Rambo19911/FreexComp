using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

/// <summary>Visual subjects of the References result set of Search Results (F13-016a, I-353).</summary>
public sealed partial class VisualSnapshotTests
{
    /// <summary>
    /// The main window at 800 × 800 and 1920 × 1040, light and dark, with the bottom dock enlarged and Search Results
    /// showing: <c>search-results-empty</c> (never searched); <c>search-results-references</c> — a virtual method of a
    /// library class that the project overrides (badges D, I, C; the library file row last, collapsed, with its
    /// suffix); <c>search-results-references-field</c> (D, W, R in three files); <c>search-results-references-type</c>
    /// (D, T). Rules (UI-TW-609…612): every row of the set is drawn completely inside the window, each level starts
    /// further in, each badge draws its letter in the colour of its token inside its 16 × 16 slot, and nothing is
    /// clipped or overlaps.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "UI-TW-609")]
    [Trait("Req", "UI-TW-610")]
    [Trait("Req", "UI-TW-611")]
    [Trait("Req", "UI-TW-612")]
    public async Task SearchResultsReferences_RendersLevelsBadgesAndLibraryRow_NoClippingOrOverlap()
    {
        if (!SkiaProcess.IsCurrent)
        {
            await SkiaProcess.RunThisTestAsync(typeof(VisualSnapshotTests));
            return;
        }

        const string empty = "search-results-empty";
        (string Subject, string File, string Source, string At, string Header, string Letters)[] queries =
        [
            ("search-results-references", "Hello.pas", ReferencesViewTests.Main, "C.Draw", "References to Draw — 7 references in 3 files (3 in library sources)", "CDI"),
            ("search-results-references-field", "Unit2.pas", ReferencesViewTests.Unit2, "Count >", "References to Count — 6 references in 3 files", "DRW"),
            ("search-results-references-type", "Unit2.pas", ReferencesViewTests.Unit2, "TCounter", "References to TCounter — 7 references in 3 files", "DT"),
        ];
        string[] subjects = [empty, .. queries.Select(q => q.Subject)];
        Size[] sizes = [new(800, 800), new(1920, 1040)];
        foreach (var subject in subjects)
        {
            if (Directory.Exists(VisualSnapshot.FolderOf(subject)))
                Directory.Delete(VisualSnapshot.FolderOf(subject), recursive: true);
        }

        var defects = new List<(string Variant, string Defect)>();
        var files = new List<(string Subject, string Theme, Size Size, SnapshotFile File)>();
        foreach (var theme in Themes)
        {
            await using var h = await ReferencesViewTests.StartAsync(o => o.Settings["appearance.theme"] = theme);
            var results = h.Services.SearchResults;

            void Shoot(string subject, Action<string> check)
            {
                foreach (var size in sizes)
                {
                    VisualSnapshot.Arrange(h.Window, size, 1.0);
                    h.Drag(AutomationIds.Splitter(AutomationIds.DockBottom), 0, -2000); // the tallest the bottom dock may be
                    var variant = $"{subject} {VisualSnapshot.FileName(theme, 1.0, size)}";
                    Assert.Equal(size, h.Window.ClientSize);
                    check(variant);
                    defects.AddRange(VisualSnapshot.LayoutDefects(h.Find<Control>(AutomationIds.DockBottom)).Select(d => (variant, d)));
                    files.Add((subject, theme, size, VisualSnapshot.Capture(h.Window, subject, theme)));
                }
            }

            // Never searched: the empty state.
            await h.ExecuteAsync("view.searchResults");
            Shoot(empty, variant =>
            {
                var text = h.Find<TextBlock>("Tool.SearchResults.Empty");
                Assert.True(text.IsEffectivelyVisible, $"{variant}: the empty state is not shown");
                if (VisualSnapshot.HiddenPart(text, h.Window) is { } hidden)
                    defects.Add((variant, "cut: the empty state — " + hidden));
                Assert.Null(h.TryFind<Control>("Tool.SearchResults.Row."));
            });

            foreach (var query in queries)
            {
                await ReferencesViewTests.FindAsync(h, query.File, query.Source, query.At, query.Header);
                Shoot(query.Subject, variant =>
                {
                    var header = h.Find<TextBlock>("Tool.SearchResults.Header");
                    Assert.Equal(query.Header, header.Text);
                    if (VisualSnapshot.TextWidth(header) > header.Bounds.Width + 0.5)
                        defects.Add((variant, "the header is trimmed"));
                    var letters = new HashSet<string>();
                    double fileLeft = -1, containerLeft = -1, referenceLeft = -1;
                    foreach (var row in results.Rows)
                    {
                        var control = h.TryFind<Control>("Tool.SearchResults.Row." + row.Key);
                        if (control is not { IsEffectivelyVisible: true })
                        {
                            defects.Add((variant, $"the row {row.Key} is not shown"));
                            continue;
                        }

                        if (VisualSnapshot.HiddenPart(control, h.Window) is { } hidden)
                            defects.Add((variant, $"cut: the row {row.Key} — {hidden}"));
                        if (VisualSnapshot.DistinctColors(h.Window, control) < 2)
                            defects.Add((variant, $"the row {row.Key} is not drawn"));
                        var left = control.TranslatePoint(default, h.Window)!.Value.X;
                        if (row.ReferenceKind is not { } kind)
                        {
                            if (row.IsFile)
                                fileLeft = left;
                            else
                                containerLeft = left;
                            continue;
                        }

                        // The badge: its letter in the colour of its token, inside the slot.
                        referenceLeft = left;
                        var badge = h.Find<Border>("Tool.SearchResults.Row." + row.Key + ".Kind");
                        var letter = (TextBlock)badge.Child!;
                        letters.Add(letter.Text!);
                        if (VisualSnapshot.HiddenPart(letter, h.Window) is { } outside)
                            defects.Add((variant, $"cut: the badge of {row.Key} — {outside}"));
                        var colour = Token(h.Window, SearchResultsViewModel.KindToken(kind));
                        var slot = VisualSnapshot.PixelsOf(h.Window, new Rect(badge.TranslatePoint(default, h.Window)!.Value, badge.Bounds.Size));
                        var drawn = InkPixels(slot, colour);
                        if (drawn < 3)
                            defects.Add((variant, $"the badge {letter.Text} of {row.Key} is not drawn in {SearchResultsViewModel.KindToken(kind)} ({(drawn < 0 ? "another colour" : drawn + " pixels")})"));
                    }

                    Assert.Equal(query.Letters, string.Concat(letters.Order(StringComparer.Ordinal)));
                    if (!(fileLeft < containerLeft && containerLeft < referenceLeft))
                        defects.Add((variant, $"the levels are not indented: {fileLeft}, {containerLeft}, {referenceLeft}"));
                    if (query.Subject == queries[0].Subject)
                    {
                        // The library file row: last, collapsed, with the suffix after its count.
                        var library = results.Rows[^1];
                        Assert.True(library is { IsFile: true, IsLibrary: true, IsExpanded: false }, $"{variant}: the last row is not the collapsed library file row");
                    }
                });
            }
        }

        AssertSnapshots(files, subjects, 4, defects);
    }

    /// <summary>
    /// The number of pixels of the area that are drawn with <paramref name="ink"/> on its background (the most frequent
    /// colour): each colour channel lies between the background and the ink — the smoothed edges of a thin letter are
    /// blends of both, per channel — and the pixel is at least a third ink. -1 when a pixel lies outside that span: the
    /// area holds another colour. Either byte order of the frame.
    /// </summary>
    private static int InkPixels(int[] pixels, Color ink)
    {
        const double tolerance = 16;
        static double[] Bytes(int p) => [p & 0xFF, (p >> 8) & 0xFF, (p >> 16) & 0xFF];
        var background = Bytes(pixels.GroupBy(p => p).OrderByDescending(g => g.Count()).First().Key);
        int Count(double[] to)
        {
            var count = 0;
            foreach (var pixel in pixels)
            {
                var bytes = Bytes(pixel);
                double covered = 0;
                for (var c = 0; c < 3; c++)
                {
                    var (low, high) = (Math.Min(background[c], to[c]), Math.Max(background[c], to[c]));
                    if (bytes[c] < low - tolerance || bytes[c] > high + tolerance)
                        return -1;
                    covered += high - low < 1 ? 0 : Math.Abs(bytes[c] - background[c]) / (high - low);
                }

                if (covered / 3 >= 1.0 / 3)
                    count++;
            }

            return count;
        }

        return Math.Max(Count([ink.B, ink.G, ink.R]), Count([ink.R, ink.G, ink.B]));
    }
}
