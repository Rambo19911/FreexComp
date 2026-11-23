using FreexComp.Designer.Layout;
using FreexComp.Designer.Model;
using FreexComp.Designer.Surface;
using FreexComp.Designer.Tests.Support;
using FreexComp.Designer.Text;
using static FreexComp.Designer.Tests.Layout.LayoutHarness;

namespace FreexComp.Designer.Tests.Layout;

/// <summary>General rules of the layout engine (layout-engine.md §1 … §5; D-087) on the framework metadata.</summary>
[Trait("Phase", "5")]
public sealed class LayoutEngineRuleTests
{
    private static FormDocument Sample() => Form(400, 240,
        Obj("Top1", "TPanel", 0, 0, 400, 30, "Align = alTop")
        + Obj("Client1", "TPanel", 0, 30, 400, 210, "Align = alClient", Obj("Inner", "TButton", 10, 10, 75, 25, "Anchors = [akLeft, akTop, akRight]"))
        + Obj("Btn", "TButton", 232, 15, 75, 25, "Anchors = [akRight, akBottom]"));

    private static IReadOnlyList<string> Compare(LayoutDumpForm[] designer, LayoutDumpForm[] runtime) =>
        LayoutDump.Compare(new LayoutDumpDocument(LayoutDump.FormatVersion, LayoutDump.Dpi, designer),
            new LayoutDumpDocument(LayoutDump.FormatVersion, LayoutDump.Dpi, runtime));

    [Fact]
    [Trait("Req", "DSG-LAY-004")]
    public void ComputeDoesNotModifyTheDocument()
    {
        var form = Sample();
        var before = FormWriter.Write(form);
        var version = form.Version;

        _ = Engine.Compute(form);
        _ = Engine.Compute(form, LayoutMode.RunTime);
        _ = Engine.ComputeWithResize(form, "LayoutForm", 600, 400);

        Assert.Equal(before, FormWriter.Write(form));
        Assert.Equal(version, form.Version);
    }

    [Fact]
    [Trait("Req", "DSG-LAY-003")]
    [Trait("Req", "DSG-ARCH-005")]
    public void IntegerPixelsAt96DpiWithoutScaling()
    {
        // A form saved at another PixelsPerInch is not scaled: ChangeScale is the identity, bounds are the stored integers.
        var form = Parse("""
            object LayoutForm: TForm
              ClientWidth = 400
              ClientHeight = 240
              PixelsPerInch = 144
              object Btn: TButton
                Left = 33
                Top = 17
                Width = 75
                Height = 25
              end
            end
            """);
        var result = Engine.Compute(form);
        Assert.Equal(new Rect(33, 17, 75, 25), Bounds(result, form, "Btn"));
        Assert.Equal(new Rect(0, 0, 400, 240), result.GetBounds(form.Root));

        // Odd sizes stay integral through anchoring: one pixel more moves the right/bottom-anchored button by one pixel.
        var sample = Sample();
        var odd = Engine.ComputeWithResize(sample, "LayoutForm", 401, 241);
        Assert.Equal(new Rect(233, 16, 75, 25), Bounds(odd, sample, "Btn"));
    }

    [Fact]
    [Trait("Req", "DSG-LAY-022")]
    public void ConstraintsApplyToTheSizeAfterAnchors()
    {
        // Anchored left and right: the parent grows by 200, the width would be 300, MaxWidth caps it at 150 — the left
        // edge is not recomputed (it stays at 10, the right edge does not follow).
        var form = Form(200, 100, Obj("Edit1", "TEdit", 10, 10, 100, 23, "Anchors = [akLeft, akTop, akRight]\nConstraints.MaxWidth = 150"));
        var grown = Engine.ComputeWithResize(form, "LayoutForm", 400, 100);
        Assert.Equal(new Rect(10, 10, 150, 23), Bounds(grown, form, "Edit1"));

        // Anchored right only: the control moves with the right edge; a MinWidth larger than the width grows it to the
        // right from the moved left edge.
        form = Form(200, 100, Obj("Edit1", "TEdit", 90, 10, 100, 23, "Anchors = [akTop, akRight]\nConstraints.MinWidth = 120"));
        grown = Engine.ComputeWithResize(form, "LayoutForm", 300, 100);
        Assert.Equal(new Rect(190, 10, 120, 23), Bounds(grown, form, "Edit1"));
    }

    [Fact]
    [Trait("Req", "DSG-LAY-033")]
    public void FormOwnPositionAlignAndConstraintsAreIgnored()
    {
        var form = Parse("""
            object LayoutForm: TForm
              Left = 200
              Top = 120
              Constraints.MaxWidth = 100
              Constraints.MaxHeight = 50
              ClientWidth = 400
              ClientHeight = 240
              object Btn: TButton
                Left = 8
                Top = 8
                Width = 75
                Height = 25
              end
            end
            """);
        var result = Engine.Compute(form);
        Assert.Equal(new Rect(0, 0, 400, 240), result.GetBounds(form.Root));
        Assert.Equal(new Rect(8, 8, 75, 25), Bounds(result, form, "Btn"));
        Assert.Empty(result.Differences());
    }

    [Fact]
    [Trait("Req", "DSG-LAY-042")]
    public void InconsistentStoredBoundsStillGiveOneDeterministicLayout()
    {
        // Stored bounds that disagree with Align (the alTop panel stored 50 wide, the alClient one at the wrong place):
        // the layout is computed from the stored values by the load sequence, the same every time, in both modes.
        var inconsistent = Form(400, 240,
            Obj("Top1", "TPanel", 7, 9, 50, 30, "Align = alTop")
            + Obj("Client1", "TPanel", 3, 3, 10, 10, "Align = alClient"));
        var design = Engine.Compute(inconsistent);
        var runtime = Engine.Compute(inconsistent, LayoutMode.RunTime);
        Assert.NotEmpty(design.Differences()); // the invariant does not hold for this file
        Assert.Equal(new Rect(0, 0, 400, 30), Bounds(design, inconsistent, "Top1"));
        Assert.Equal(new Rect(0, 30, 400, 210), Bounds(design, inconsistent, "Client1"));
        Assert.Equal(Bounds(design, inconsistent, "Top1"), Bounds(runtime, inconsistent, "Top1"));
        Assert.Equal(Bounds(design, inconsistent, "Client1"), Bounds(runtime, inconsistent, "Client1"));
        Assert.Equal(LayoutDump.Serialize([Engine.Compute(inconsistent).ToDump()]), LayoutDump.Serialize([design.ToDump()]));

        // The consistent file of the same form gives the same layout.
        var consistent = Form(400, 240,
            Obj("Top1", "TPanel", 0, 0, 400, 30, "Align = alTop")
            + Obj("Client1", "TPanel", 0, 30, 400, 210, "Align = alClient"));
        Assert.Empty(Compare([Engine.Compute(consistent, LayoutMode.RunTime).ToDump()], [runtime.ToDump()]));
    }

    [Fact]
    [Trait("Req", "DSG-LAY-054")]
    public void DumpComparisonIsExactWithoutTolerance()
    {
        var a = Engine.Compute(Sample(), LayoutMode.RunTime).ToDump();
        var shifted = Engine.Compute(Form(400, 240,
            Obj("Top1", "TPanel", 0, 0, 400, 31, "Align = alTop")
            + Obj("Client1", "TPanel", 0, 31, 400, 209, "Align = alClient", Obj("Inner", "TButton", 10, 10, 75, 25, "Anchors = [akLeft, akTop, akRight]"))
            + Obj("Btn", "TButton", 232, 15, 75, 25, "Anchors = [akRight, akBottom]")), LayoutMode.RunTime).ToDump();

        // One pixel is a difference, reported per field with its path; the same layout compares equal.
        Assert.Empty(Compare([a], [Engine.Compute(Sample(), LayoutMode.RunTime).ToDump()]));
        var differences = Compare([a], [shifted]);
        Assert.Contains("LayoutForm.Top1: height designer=30 runtime=31", differences);
        Assert.Contains("LayoutForm.Client1: top designer=30 runtime=31", differences);
        Assert.Contains("LayoutForm.Client1: height designer=210 runtime=209", differences);
    }

    [Fact]
    [Trait("Req", "DSG-LAY-002")]
    public void InteractiveResizeStoresTheRecomputedChildBounds()
    {
        using var h = DesignerHarness.FromText("""
            object F: TForm
              ClientWidth = 400
              ClientHeight = 300
              object P: TPanel
                Left = 8
                Top = 8
                Width = 200
                Height = 120
                TabOrder = 0
                object Head: TPanel
                  Left = 1
                  Top = 1
                  Width = 198
                  Height = 30
                  Align = alTop
                  TabOrder = 0
                end
                object Ok: TButton
                  Left = 117
                  Top = 88
                  Width = 75
                  Height = 25
                  Anchors = [akRight, akBottom]
                  TabOrder = 1
                end
              end
            end

            """);
        h.Settings.NoSnapping();
        var head = h.Bounds("Head");
        var ok = h.Bounds("Ok");

        // The designer resizes the container by its SE handle: the engine re-flows the children and the designer stores
        // the results, so the file stays self-consistent (stored = computed, DSG-LAY-040).
        h.Select("P");
        h.PressHandle("P", HandleKind.SE);
        h.MoveBy(40, 20);
        h.Release();
        Assert.Equal((240, 140), h.Size("P"));
        Assert.Equal(head with { Width = head.Width + 40 }, h.Bounds("Head"));
        Assert.Equal(ok with { Left = ok.Left + 40, Top = ok.Top + 20 }, h.Bounds("Ok"));
        Assert.Contains("      Width = 238\n", h.Text, StringComparison.Ordinal);
        Assert.Contains($"      Left = {ok.Left + 40}\n      Top = {ok.Top + 20}\n", h.Text, StringComparison.Ordinal);
        Assert.Empty(h.Engine.Compute(h.Document).Differences());

        // One undo unit restores every stored value.
        h.ExecuteUndo();
        Assert.Equal(head, h.Bounds("Head"));
        Assert.Equal(ok, h.Bounds("Ok"));
    }

    [Fact]
    [Trait("Req", "DSG-LAY-064")]
    public void CorpusCoversTheFormerSampleSituations()
    {
        // align basics E01 … E06, margins E07/E08/E36, constraints E12 … E15/E22, anchors E16 … E23, nested containers E24 … E27.
        var names = Directory.EnumerateFiles(CorpusFolder, "*.form").Select(Path.GetFileName).ToList();
        int[] cases = [1, 2, 3, 4, 5, 6, 7, 8, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 36];
        foreach (var n in cases)
        {
            var prefix = $"E{n:00}";
            var forms = names.Where(f => f!.StartsWith(prefix, StringComparison.Ordinal) && f.Length > 3 && !char.IsDigit(f[3])).ToList();
            Assert.True(forms.Count > 0, $"no corpus case {prefix}");
            Assert.All(forms, f => Assert.True(File.Exists(Path.Combine(CorpusFolder, Path.ChangeExtension(f!, ".expected.json"))), f + " has no expected dump"));
        }

        // The gallery sample exercises them through the real start-up path.
        Assert.True(File.Exists(Path.Combine(RepoRoot, "ui-framework", "samples", "layout-gallery", "Gallery.pproj")));
    }
}
