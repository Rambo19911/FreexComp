using System.Globalization;
using FreexComp.Debugger;
using FreexComp.Ide.Core.Debugging;

namespace FreexComp.Ide.Core.Tests.Debugging;

/// <summary>DBG-258 (I-223): the value of a tooltip is expandable like a row of Locals (UI-TW-1001, UI-TW-1007).</summary>
[Trait("Phase", "8")]
[Trait("Category", "Unit")]
public sealed class HoverValueNodeTests
{
    private static readonly VariableHandle Array = new(1, 7);

    private static VariableInfo Value(string name, string value, VariableHandle? children = null, int count = 0) =>
        new(name, value, "Integer", children is null ? ValueKind.Integer : ValueKind.DynamicArray, children, count, false, name, null);

    /// <summary>More than 100 children: ranges of 100, read when each is expanded; keys are paths of the value, not of the range.</summary>
    [Fact]
    [Trait("Req", "DBG-258")]
    [Trait("Req", "UI-TW-1001")]
    [Trait("Req", "UI-TW-1007")]
    public async Task ExpandsInRangesOfOneHundred()
    {
        var reads = new List<(VariableHandle Handle, int Start, int Count)>();
        Task<IReadOnlyList<VariableInfo>> Read(VariableHandle handle, int start, int count)
        {
            reads.Add((handle, start, count));
            return Task.FromResult<IReadOnlyList<VariableInfo>>(
                [.. Enumerable.Range(start, count).Select(i => Value(string.Create(CultureInfo.InvariantCulture, $"[{i}]"), (i * 10).ToString(CultureInfo.InvariantCulture)))]);
        }

        var root = HoverValueNode.Root("Items", Value("Items", "(0, 10, …)", Array, 250), Read);
        var changes = 0;
        root.Changed += (_, _) => changes++;
        Assert.True(root.CanExpand);
        Assert.Null(root.Children);

        await root.ToggleAsync();
        Assert.True(root.IsExpanded);
        Assert.Empty(reads); // the ranges are made without a request
        Assert.Equal(["[0..99]", "[100..199]", "[200..249]"], root.Children!.Select(c => c.Name));
        Assert.Equal(["Items[0..99]", "Items[100..199]", "Items[200..249]"], root.Children!.Select(c => c.Key));
        Assert.All(root.Children!, c => Assert.Equal((true, false, 1, null), (c.CanExpand, c.IsExpanded, c.Level, c.Info)));

        var last = root.Children![2];
        await last.ToggleAsync();
        Assert.Equal([(Array, 200, 50)], reads);
        Assert.Equal(("Items[200]", "[200]", "2000", 2), (last.Children![0].Key, last.Children[0].Name, last.Children[0].Info!.Value, last.Children[0].Level));
        Assert.Equal("Items[249]", last.Children[^1].Key);
        Assert.False(last.Children[0].CanExpand);

        // Collapsed and expanded again: no second request.
        await last.ToggleAsync();
        await last.ToggleAsync();
        Assert.Single(reads);
        Assert.True(changes >= 4);
    }

    /// <summary>At most 100 children: read at once (count 0 = all, DBG-411); fields get <c>.Name</c> keys; what cannot be read is empty.</summary>
    [Fact]
    [Trait("Req", "DBG-258")]
    public async Task ReadsFewChildrenAtOnce()
    {
        var record = new VariableHandle(1, 3);
        var root = HoverValueNode.Root("P", Value("P", "(X: 3; Y: 4)", record, 2),
            (handle, start, count) => Task.FromResult<IReadOnlyList<VariableInfo>>(handle == record && (start, count) == (0, 0) ? [Value("X", "3"), Value("Y", "4")] : []));
        await root.ToggleAsync();
        Assert.Equal([("P.X", "X", "3"), ("P.Y", "Y", "4")], root.Children!.Select(c => (c.Key, c.Name, c.Info!.Value)));

        var gone = HoverValueNode.Root("Q", Value("Q", "(…)", record, 2),
            (_, _, _) => Task.FromException<IReadOnlyList<VariableInfo>>(new ObjectDisposedException("session")));
        await gone.ToggleAsync();
        Assert.Empty(gone.Children!);

        var leaf = HoverValueNode.Root("I", Value("I", "3"), (_, _, _) => throw new InvalidOperationException("not read"));
        Assert.False(leaf.CanExpand);
        await leaf.ToggleAsync();
        Assert.False(leaf.IsExpanded);
    }
}
