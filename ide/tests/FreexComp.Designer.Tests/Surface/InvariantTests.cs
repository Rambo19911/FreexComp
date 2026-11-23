using System.Text;
using FreexComp.Designer.Surface;
using FreexComp.Designer.Text;
using FreexComp.Designer.Tests.Support;

namespace FreexComp.Designer.Tests.Surface;

/// <summary>The stored = computed layout invariant (layout-engine.md DSG-LAY-040, DSG-LAY-041; D-056).</summary>
public sealed class InvariantTests
{
    /// <summary>Aligned panels, nested containers, anchored controls: stored bounds consistent with the layout.</summary>
    private const string Form = """
        object F: TForm
          ClientWidth = 480
          ClientHeight = 360
          object TopBar: TPanel
            Left = 0
            Top = 0
            Width = 480
            Height = 41
            Align = alTop
            TabOrder = 0
            object Go: TButton
              Left = 8
              Top = 8
              Width = 75
              Height = 25
              TabOrder = 0
            end
          end
          object Side: TPanel
            Left = 0
            Top = 41
            Width = 120
            Height = 319
            Align = alLeft
            TabOrder = 1
          end
          object Body: TPanel
            Left = 120
            Top = 41
            Width = 360
            Height = 319
            Align = alClient
            TabOrder = 2
            object Box: TGroupBox
              Left = 16
              Top = 16
              Width = 185
              Height = 105
              Caption = 'Box'
              TabOrder = 0
              object Chk: TCheckBox
                Left = 16
                Top = 24
                Width = 97
                Height = 17
                TabOrder = 0
              end
            end
            object Note: TEdit
              Left = 216
              Top = 16
              Width = 121
              Height = 23
              Anchors = [akLeft, akTop, akRight]
              TabOrder = 1
            end
            object Ok: TButton
              Left = 264
              Top = 280
              Width = 75
              Height = 25
              Anchors = [akRight, akBottom]
              TabOrder = 2
            end
          end
        end

        """;

    private static readonly HandleKind[] Handles = Enum.GetValues<HandleKind>();

    private static readonly DesignKey[] Arrows = [DesignKey.Left, DesignKey.Right, DesignKey.Up, DesignKey.Down];

    private static readonly InputModifiers[] ArrowModifiers =
        [InputModifiers.None, InputModifiers.Ctrl, InputModifiers.Shift, InputModifiers.Shift | InputModifiers.Alt];

    /// <summary>
    /// DSG-LAY-040, property-based: 200 sequences (seeds 1 … 200) of 12 random interactions each — move and copy drags
    /// (with re-parenting), resize drags (controls and the form), keyboard nudges and resizes, align/size and z-order
    /// commands, delete, paste, placement, undo and redo — keep stored = computed after every step.
    /// </summary>
    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-LAY-040")]
    public void StoredEqualsComputedAfterRandomOperations()
    {
        for (var seed = 1; seed <= 200; seed++)
        {
            RunSequence(seed, steps: 12);
        }
    }

    private static void RunSequence(int seed, int steps)
    {
        var random = new Random(seed);
        var log = new StringBuilder();
        using var h = DesignerHarness.FromText(Form);
        h.SkipFinalChecks = true; // asserted below with the seed in the message
        try
        {
            h.AssertInvariant();
            for (var step = 0; step < steps; step++)
            {
                h.Settings.SnapToGrid = random.Next(2) == 0;
                h.Settings.UseGuidelines = random.Next(2) == 0;
                log.Append(step).Append(": ").AppendLine(RandomOperation(h, random));
                h.AssertInvariant();
                Assert.Equal(InteractionState.Idle, h.Controller.State);
            }

            var reparsed = FormParser.Parse(h.Text, "check.form", TestCatalog.Instance);
            Assert.True(reparsed.Document is { Diagnostics.Count: 0 }, string.Join('\n', reparsed.Diagnostics));
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            throw new Xunit.Sdk.XunitException($"seed {seed} failed after:\n{log}\n{e.Message}\n\n{h.Text}");
        }
    }

    /// <summary>Performs one random interaction and describes it.</summary>
    private static string RandomOperation(DesignerHarness h, Random random)
    {
        var components = h.Document.AllObjects().Where(o => o.Parent is not null).Select(o => o.Name).ToList();
        string Pick() => components[random.Next(components.Count)];
        int Delta() => random.Next(-48, 49);

        switch (random.Next(11))
        {
            case 0 when components.Count > 0:
            {
                var on = Pick();
                var (dx, dy) = (Delta(), Delta());
                var ctrl = random.Next(4) == 0;
                h.Select(on);
                h.Press(on);
                h.MoveBy(dx / 2, dy / 2);
                h.MoveBy(dx - dx / 2, dy - dy / 2);
                h.Release(ctrl ? InputModifiers.Ctrl : InputModifiers.None);
                return $"{(ctrl ? "copy" : "move")} drag {on} by ({dx}, {dy})";
            }

            case 1 when components.Count > 0:
            {
                var on = Pick();
                var handle = Handles[random.Next(Handles.Length)];
                var (dx, dy) = (Delta(), Delta());
                h.Select(on);
                h.PressHandle(on, handle);
                h.MoveBy(dx, dy);
                h.Release();
                return $"resize drag {on} {handle} by ({dx}, {dy})";
            }

            case 2 when components.Count > 0:
            {
                var on = Pick();
                var key = Arrows[random.Next(Arrows.Length)];
                var modifiers = ArrowModifiers[random.Next(ArrowModifiers.Length)];
                h.Select(on);
                h.Key(key, modifiers);
                h.Time.Advance(TimeSpan.FromMilliseconds(random.Next(0, 2000)));
                return $"key {modifiers} {key} on {on}";
            }

            case 3 when components.Count > 1:
            {
                var names = Enumerable.Range(0, random.Next(2, 4)).Select(_ => Pick()).Distinct().ToArray();
                var command = Commands.Arrange[random.Next(Commands.Arrange.Count)];
                h.Select(names);
                h.Execute(command);
                return $"{command} on {string.Join(", ", names)}";
            }

            case 4 when components.Count > 0:
            {
                var on = Pick();
                var command = random.Next(2) == 0 ? Commands.BringToFront : Commands.SendToBack;
                h.Select(on);
                h.Execute(command);
                return $"{command} on {on}";
            }

            case 5 when components.Count > 3:
            {
                var on = Pick();
                h.Select(on);
                h.Execute(Commands.Delete);
                return $"delete {on}";
            }

            case 6 when components.Count > 0:
            {
                var on = Pick();
                h.Select(on);
                h.Execute(Commands.Copy);
                h.Execute(Commands.Paste);
                return $"copy and paste {on}";
            }

            case 7:
            {
                var className = random.Next(2) == 0 ? "TButton" : "TPanel";
                var point = h.Controller.Geometry.FormClientToSurface((random.Next(0, 480), random.Next(0, 360)));
                h.Host.Arm(className);
                h.ClickAt(point);
                return $"place {className} at {point}";
            }

            case 8:
            {
                var redo = random.Next(3) == 0;
                h.Execute(redo ? Commands.Redo : Commands.Undo);
                return redo ? "redo" : "undo";
            }

            case 9:
            {
                var (dx, dy) = (Delta(), Delta());
                h.Select("F");
                h.PressHandle("F", HandleKind.SE);
                h.MoveBy(dx, dy);
                h.Release();
                return $"form resize by ({dx}, {dy})";
            }

            default:
            {
                h.Execute(Commands.Undo);
                return "undo";
            }
        }
    }

    /// <summary>
    /// DSG-LAY-041: a loaded form that violates the invariant is shown with computed bounds and is not modified; the first
    /// undo unit of any kind stores the differences, and undoing it restores the file's original values.
    /// </summary>
    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-LAY-041")]
    public void LoadedViolationIsStoredWithFirstEdit()
    {
        // TopBar is aligned to the top but stores the width 300 (computed: the client width 480).
        var violating = Form.Replace("""
                Width = 480
                Height = 41
                Align = alTop
            """, """
                Width = 300
                Height = 41
                Align = alTop
            """, StringComparison.Ordinal);
        Assert.NotEqual(Form, violating);

        (string Name, Action<DesignerHarness> Edit)[] firstEdits =
        [
            ("keyboard nudge", h => { h.Select("Chk"); h.Key(DesignKey.Right); }),
            ("move drag", h => { h.Select("Chk"); h.Press("Chk"); h.MoveBy(10, 10); h.Release(); }),
            ("resize drag", h => { h.Select("Box"); h.PressHandle("Box", HandleKind.SE); h.MoveBy(10, 10); h.Release(); }),
            ("align command", h => { h.Select("Note", "Box"); h.Execute(Commands.AlignLeft); }),
            ("z-order command", h => { h.Select("Box"); h.Execute(Commands.BringToFront); }),
            ("delete", h => { h.Select("Chk"); h.Execute(Commands.Delete); }),
            ("paste", h => { h.Select("Chk"); h.Execute(Commands.Copy); h.Execute(Commands.Paste); }),
            ("placement", h => { h.Host.Arm("TButton"); h.ClickAt(h.Controller.Geometry.FormClientToSurface((300, 200))); }),
            ("default handler", h => { h.Select("Ok"); h.Execute(Commands.CreateDefaultHandler); }),
            ("tab-order click", h => { h.Execute(Commands.TabOrder); h.Click("Ok"); h.Controller.ExitTabOrderMode(); }),
        ];

        foreach (var (name, edit) in firstEdits)
        {
            using var h = DesignerHarness.FromText(violating);
            h.Settings.NoSnapping();
            h.SkipFinalChecks = true; // the loaded form violates the invariant by design; checked after the edit below
            var original = h.Text;

            // Shown with computed bounds, the document is not modified.
            Assert.NotEmpty(h.Engine.Compute(h.Document).Differences());
            Assert.Equal(480, h.Bounds("TopBar").Width);
            Assert.True(h.Undo.IsAtSavePoint);
            Assert.Equal(0, h.Undo.UndoCount);
            Assert.Contains("Width = 300", original, StringComparison.Ordinal);

            edit(h);

            Assert.True(h.Undo.UndoCount == 1, $"{name}: {h.Undo.UndoCount} units");
            Assert.True(h.Engine.Compute(h.Document).Differences().Count == 0, $"{name}: the first unit did not store the differences");
            Assert.False(h.Undo.IsAtSavePoint);

            h.ExecuteUndo();
            Assert.True(original == h.Text, $"{name}: undo did not restore the file's values");
            h.ExecuteRedo();
            Assert.True(h.Engine.Compute(h.Document).Differences().Count == 0, $"{name}: redo");
            Assert.Empty(FormParser.Parse(h.Text, "check.form", TestCatalog.Instance).Diagnostics);
        }
    }
}
