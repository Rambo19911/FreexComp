using System.Buffers.Binary;
using FreexComp.Debugger.Expressions;
using FreexComp.Debugger.Values.TypeInfo;
using FreexComp.Debugger.Values.Visualizers;

namespace FreexComp.Debugger.Values;

/// <summary>
/// The values of one stop (Pascal values §1, §8; DAP usage §5.4, §5.5): the three scopes of a frame, lazily decoded
/// children behind our own handles, raw and error nodes, and the presentation of expression results. Handles are
/// numbered from 1 per stop and die with it (architecture §8). Every value it renders from a decoded node passes the
/// visualiser selection first (visualizers.md DBG-500). Used by the session loop only.
/// </summary>
internal sealed class ValueService
{
    private readonly PdiFile? _pdi;
    private readonly IDebuggeeAccess _debuggee;
    private readonly ValueFormatOptions _options;
    private readonly ValueDecoder? _decoder;
    private readonly ValueFormatter _formatter;
    private readonly bool _canWriteMemory;
    private readonly bool _canSetVariable;
    private readonly VisualizerHost _visualizers;
    private readonly Dictionary<int, Handle> _handles = [];
    private int _lastHandle;
    private int _epoch;

    /// <summary>The name of the last child of a visualised node: the plain rendering (DBG-506).</summary>
    public const string RawChildName = "[Raw]";

    /// <summary>What a handle stands for: a decoded node whose children are decoded on demand, or a list made on first use.</summary>
    private sealed class Handle
    {
        public ValueNode? Node { get; init; }

        /// <summary>The node's children are decoded in raw mode (DBG-508).</summary>
        public bool Raw { get; init; }

        /// <summary>The node is visualised: its children are the visualiser's (or the plain ones), then <c>[Raw]</c>.</summary>
        public Visualized? Visual { get; init; }
        public Func<CancellationToken, Task<IReadOnlyList<VariableInfo>>>? Load { get; init; }

        /// <summary>A text handle (<see cref="VariableInfo.Text"/>, DBG-516): the text behind it.</summary>
        public ITextSource? Text { get; init; }
        public IReadOnlyList<VariableInfo>? Items { get; set; }

        /// <summary>Children of <see cref="Node"/> already handed out: the same child keeps its handle.</summary>
        public Dictionary<int, VariableInfo> Children { get; } = [];
    }

    /// <summary>A visualised node: the selected row, the context its calls share, its children (null: the plain ones).</summary>
    private sealed record Visualized(VisualizerRegistration Registration, VisualizerContext Context, VisualizerChildren? Children, ValueFormatter Formatter)
    {
        /// <summary>Every child including <c>[Raw]</c>.</summary>
        public int Count(ValueNode node) => (Children is { } children ? Math.Max(children.Count, 0) : node.ChildCount) + 1;
    }

    /// <param name="pdi">Type information of the program; null: values are shown as the adapter prints them.</param>
    /// <param name="debuggee">Memory, addresses and the adapter's own variable lists.</param>
    /// <param name="options">Display options.</param>
    /// <param name="canWriteMemory">The adapter can write memory.</param>
    /// <param name="canSetVariable">The adapter can set a root variable by name (fallback for root scalars, DBG-176).</param>
    /// <param name="visualizers">The session's visualisers; null: none.</param>
    public ValueService(PdiFile? pdi, IDebuggeeAccess debuggee, ValueFormatOptions options, bool canWriteMemory, bool canSetVariable = false,
        VisualizerHost? visualizers = null)
    {
        _pdi = pdi;
        _debuggee = debuggee;
        _options = options;
        _decoder = pdi is null ? null : new ValueDecoder(pdi, debuggee, options);
        _formatter = new ValueFormatter(options);
        _canWriteMemory = canWriteMemory;
        _canSetVariable = canSetVariable;
        _visualizers = visualizers ?? VisualizerHost.None;
    }

    /// <summary>A new stop: every handle of the previous one is gone; ids start at 1 again.</summary>
    public void Reset(int epoch)
    {
        _handles.Clear();
        _lastHandle = 0;
        _epoch = epoch;
    }

    /// <summary>Always exactly three scopes in this order (DBG-079); their children are made when asked for.</summary>
    public IReadOnlyList<VariableScope> GetScopes(FrameContext frame) =>
    [
        Scope(ScopeKind.Arguments, frame),
        Scope(ScopeKind.Locals, frame),
        Scope(ScopeKind.Globals, frame),
    ];

    private VariableScope Scope(ScopeKind kind, FrameContext frame) =>
        new(kind, kind.ToString(), NewHandle(new Handle { Load = ct => LoadScopeAsync(kind, frame, ct) }));

    /// <summary>
    /// Children <paramref name="start"/> … of a handle: <paramref name="count"/> of them, or with 0 up to the page
    /// size (DBG-411). A handle of another stop faults with <c>StaleReference</c> before anything is requested (DBG-408).
    /// </summary>
    public async Task<IReadOnlyList<VariableInfo>> GetChildrenAsync(VariableHandle handle, int start, int count, CancellationToken ct)
    {
        if (handle.Epoch != _epoch || !_handles.TryGetValue(handle.Id, out var entry))
            throw new DebuggerException(DebugErrorCode.StaleReference, Messages.StaleHandle);

        start = Math.Max(start, 0);
        var limit = count <= 0 ? Math.Max(_options.ChildPageSize, 1) : count;
        if (entry.Node is not { } node)
        {
            // A text handle has no children.
            if (entry.Load is null)
                return [];
            entry.Items ??= await entry.Load(ct).ConfigureAwait(false);
            return [.. entry.Items.Skip(start).Take(limit)];
        }

        var total = entry.Visual?.Count(node) ?? node.ChildCount;
        var end = (int)Math.Min(total, (long)start + limit);
        var children = new List<VariableInfo>(Math.Max(end - start, 0));
        for (var index = start; index < end; index++)
        {
            if (!entry.Children.TryGetValue(index, out var child))
            {
                child = await ChildAsync(entry, node, index, total, ct).ConfigureAwait(false);
                entry.Children.Add(index, child);
            }

            children.Add(child);
        }

        return children;
    }

    /// <summary>Elements one text chunk holds at most (DBG-430).</summary>
    public const int MaxTextChunk = 65536;

    /// <summary>DBG-429: the details of the text behind a text handle.</summary>
    public Task<TextDetails> GetTextDetailsAsync(VariableHandle text, CancellationToken ct) => TextOf(text).DetailsAsync(ct);

    /// <summary>
    /// DBG-430: one chunk of the text behind a text handle, <paramref name="count"/> clamped to 1 … 65 536 elements;
    /// a negative <paramref name="start"/> is an <see cref="ArgumentOutOfRangeException"/>.
    /// </summary>
    public Task<TextChunk> ReadTextAsync(VariableHandle text, long start, int count, CancellationToken ct)
    {
        var source = TextOf(text);
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        return source.ReadAsync(start, Math.Clamp(count, 1, MaxTextChunk), ct);
    }

    /// <summary>The text of a handle of this stop; any other handle faults with <c>StaleReference</c> (DBG-408).</summary>
    private ITextSource TextOf(VariableHandle handle) =>
        handle.Epoch == _epoch && _handles.TryGetValue(handle.Id, out var entry) && entry.Text is { } text
            ? text
            : throw new DebuggerException(DebugErrorCode.StaleReference, Messages.StaleHandle);

    // ---- scopes ----

    private async Task<IReadOnlyList<VariableInfo>> LoadScopeAsync(ScopeKind kind, FrameContext frame, CancellationToken ct)
    {
        if (_decoder is null || frame.Routine is not { } routine)
        {
            // No type information for this frame: no arguments; the rest is what the adapter lists (DBG-079).
            if (kind == ScopeKind.Arguments)
                return [];
            return await RawInfosAsync((await _debuggee.GetRawVariablesAsync(frame.AdapterFrameId, kind, ct).ConfigureAwait(false))
                .Where(variable => !IsHidden(variable.Name, routine: null)), ct).ConfigureAwait(false);
        }

        // The adapter lists parameters and locals in one scope.
        var listed = await _debuggee.GetRawVariablesAsync(
            frame.AdapterFrameId, kind == ScopeKind.Globals ? ScopeKind.Globals : ScopeKind.Locals, ct).ConfigureAwait(false);
        // The adapter prints a global with the global-scope prefix "::".
        if (kind == ScopeKind.Globals)
            listed = [.. listed.Select(v => v.Name.StartsWith("::", StringComparison.Ordinal) ? v with { Name = v.Name[2..] } : v)];
        var declared = kind switch
        {
            // Self first, then the parameters in declaration order.
            ScopeKind.Arguments => Visible(routine, frame, PdiVariableKind.Self)
                .Concat(Visible(routine, frame, PdiVariableKind.Param, PdiVariableKind.VarParam)),
            // The locals in declaration order, then the function result.
            ScopeKind.Locals => Visible(routine, frame, PdiVariableKind.Local).Concat(Visible(routine, frame, PdiVariableKind.Result)),
            _ => routine.Unit.Globals.Select(g => (Declared: g.Name, g.Type)),
        };

        var result = new List<VariableInfo>();
        foreach (var (declaredName, type) in declared)
        {
            var name = Evaluator.DisplayName(declaredName);
            // Names are matched case-insensitively; a variable the adapter does not list is not available (DBG-081).
            if (listed.FirstOrDefault(v => v.Name.Equals(declaredName, StringComparison.OrdinalIgnoreCase)) is not { } raw)
            {
                result.Add(new VariableInfo(name, Messages.NotAvailable, ValueFormatter.TypeName(type), ValueKind.Error, null, 0, false, null, null));
                continue;
            }

            if (await _debuggee.AddressOfAsync(frame.AdapterFrameId, declaredName, ct).ConfigureAwait(false) is not { } address)
            {
                // Address unobtainable: the adapter's own text (DBG-150).
                result.Add(await RawInfoAsync(raw with { Name = name }, ct).ConfigureAwait(false));
                continue;
            }

            var high = type.Kind == PdiTypeKind.OpenArray
                ? await Evaluator.OpenArrayHighAsync(_debuggee, frame, declaredName, ct).ConfigureAwait(false)
                : null;
            result.Add(await ToInfoAsync(await _decoder.DecodeAsync(name, type, address, name, ct, high).ConfigureAwait(false),
                _formatter, expandable: true, raw: false, immediate: false, ct).ConfigureAwait(false));
        }

        // A local the adapter lists but the type information does not know is shown as the adapter prints it, except
        // compiler temporaries. Not so for globals: the adapter lists the globals of the whole program there, and the
        // scope holds those of the frame's unit only (DBG-256).
        if (kind == ScopeKind.Locals)
        {
            var knownNames = routine.Variables.Select(v => v.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            result.AddRange(await RawInfosAsync(
                listed.Where(v => !knownNames.Contains(v.Name) && !IsHidden(v.Name, routine)), ct).ConfigureAwait(false));
        }

        return result;
    }

    private static IEnumerable<(string Declared, PdiType Type)> Visible(PdiRoutine routine, FrameContext frame, params PdiVariableKind[] kinds) =>
        routine.Variables.Where(v => kinds.Contains(v.Kind) && Evaluator.IsVisible(v, frame.Line)).Select(v => (v.Name, v.Type));

    /// <summary>
    /// Compiler temporaries (<c>$</c> prefix) and the hidden upper bound of an open-array parameter are never shown.
    /// </summary>
    private static bool IsHidden(string name, PdiRoutine? routine) =>
        name.StartsWith('$')
        || (name.EndsWith("$high", StringComparison.OrdinalIgnoreCase) && routine is not null
            && routine.Variables.Any(v => v.Type.Kind == PdiTypeKind.OpenArray && name.Equals(v.Name + "$high", StringComparison.OrdinalIgnoreCase)));

    // ---- raw nodes ----

    private async Task<IReadOnlyList<VariableInfo>> RawInfosAsync(IEnumerable<RawVariable> variables, CancellationToken ct)
    {
        var result = new List<VariableInfo>();
        foreach (var variable in variables)
            result.Add(await RawInfoAsync(variable, ct).ConfigureAwait(false));
        return result;
    }

    /// <summary>
    /// A variable as the adapter prints it. Its children, if it has any, are the adapter's too (DBG-080); they are
    /// fetched here once to know their number.
    /// </summary>
    private async Task<VariableInfo> RawInfoAsync(RawVariable variable, CancellationToken ct)
    {
        VariableHandle? children = null;
        var count = 0;
        if (variable.Reference > 0)
        {
            var listed = await _debuggee.GetRawChildrenAsync(variable.Reference, ct).ConfigureAwait(false);
            count = listed.Count;
            if (count > 0)
                children = NewHandle(new Handle { Load = token => RawInfosAsync(listed, token) });
        }

        return new VariableInfo(variable.Name, variable.Value, variable.Type ?? "", ValueKind.Raw, children, count, false, null, null);
    }

    // ---- nodes ----

    private VariableHandle NewHandle(Handle handle)
    {
        _handles.Add(++_lastHandle, handle);
        return new VariableHandle(_epoch, _lastHandle);
    }

    /// <summary>
    /// The public form of a node (DBG-500): the selected visualiser's summary and children with a last <c>[Raw]</c> child
    /// (DBG-505, DBG-506), or, without one or when it fails (DBG-511), the plain rendering with the nested forms of
    /// visualisers that summarise nested values (DBG-507). An expandable node gets a handle.
    /// </summary>
    /// <param name="node">The decoded node.</param>
    /// <param name="formatter">Renders it (the result's format suffix applied).</param>
    /// <param name="expandable">False when no handle may be made.</param>
    /// <param name="raw">Raw mode: no non-transparent visualiser for the node or below it (DBG-508).</param>
    /// <param name="immediate">A computed value: only visualisers whose kinds contain its kind (DBG-500).</param>
    /// <param name="ct">Cancellation.</param>
    private async Task<VariableInfo> ToInfoAsync(
        ValueNode node, ValueFormatter formatter, bool expandable, bool raw, bool immediate, CancellationToken ct)
    {
        raw |= !_options.UseVisualizers;
        // DBG-176: memory can be written, or the adapter sets root variables by name.
        var isRoot = node.EvaluateName is { } name && name.IndexOfAny(['.', '[', '^', '(']) < 0;
        var canWrite = _canWriteMemory || (_canSetVariable && isRoot);
        var selected = _pdi is null ? null : _visualizers.Select(node, raw, immediate);
        if (selected is { Transparent: false } registration)
        {
            var context = new VisualizerContext(_pdi!, _debuggee, formatter.Options, nested: false, node);
            var summary = await _visualizers.CallAsync(registration, node, context, _options.VisualizerTimeout,
                token => registration.Visualizer.SummaryAsync(node, context, token), ct).ConfigureAwait(false);
            if (summary.Ok)
            {
                var children = await _visualizers.CallAsync(registration, node, context, _options.VisualizerTimeout,
                    token => registration.Visualizer.ChildrenAsync(node, context, token), ct).ConfigureAwait(false);
                if (children.Ok)
                {
                    var visual = new Visualized(registration, context, children.Result, formatter);
                    return formatter.ToVariableInfo(node, null, canWrite) with
                    {
                        Value = summary.Result ?? "",
                        Children = expandable ? NewHandle(new Handle { Node = node, Visual = visual }) : null,
                        ChildCount = visual.Count(node),
                        Text = expandable ? TextHandle(registration, node, formatter) : null,
                    };
                }
            }
        }

        var nestedForms = raw ? null : await NestedFormsAsync(node, formatter, ct).ConfigureAwait(false);
        VariableHandle? handle = node.ChildCount > 0 && expandable ? NewHandle(new Handle { Node = node, Raw = raw }) : null;
        var info = formatter.ToVariableInfo(node, handle, canWrite, nestedForms);
        // A transparent visualiser (the string visualiser, DBG-515) keeps the plain rendering and adds its text (a handle: not when only rendered).
        return selected is { Transparent: true } transparent && expandable ? info with { Text = TextHandle(transparent, node, formatter) } : info;
    }

    /// <summary>DBG-516: a handle of this stop to the text the selected visualiser offers for a node; null for none.</summary>
    private VariableHandle? TextHandle(VisualizerRegistration registration, ValueNode node, ValueFormatter formatter)
    {
        var context = new VisualizerContext(_pdi!, _debuggee, formatter.Options, nested: false, node);
        return _visualizers.TextSource(registration, node, context) is { } text ? NewHandle(new Handle { Text = text }) : null;
    }

    /// <summary>DBG-507: the nested forms of the summary children whose visualiser summarises nested values.</summary>
    private async Task<Dictionary<ValueNode, string>?> NestedFormsAsync(ValueNode node, ValueFormatter formatter, CancellationToken ct)
    {
        Dictionary<ValueNode, string>? forms = null;
        foreach (var child in node.SummaryChildren)
        {
            if (_pdi is null || _visualizers.Select(child, raw: false, immediate: false) is not { Transparent: false, SummarizesNested: true } registration)
                continue;
            var context = new VisualizerContext(_pdi, _debuggee, formatter.Options, nested: true, child);
            var summary = await _visualizers.CallAsync(registration, child, context, _options.VisualizerTimeout,
                token => registration.Visualizer.SummaryAsync(child, context, token), ct).ConfigureAwait(false);
            if (summary.Ok)
                (forms ??= new(ReferenceEqualityComparer.Instance))[child] = summary.Result ?? "";
        }

        return forms;
    }

    /// <summary>
    /// Child <paramref name="index"/> of a node's handle: a plain child (in the handle's mode), or of a visualised node
    /// the visualiser's child (a failed one is an error node, DBG-511), the plain child, or the last, <c>[Raw]</c>.
    /// </summary>
    private async Task<VariableInfo> ChildAsync(Handle entry, ValueNode node, int index, int total, CancellationToken ct)
    {
        if (entry.Visual is not { } visual || (visual.Children is null && index < total - 1))
        {
            return await ToInfoAsync(await PlainChildAsync(node, index, ct).ConfigureAwait(false),
                _formatter, expandable: true, entry.Raw, immediate: false, ct).ConfigureAwait(false);
        }

        if (index == total - 1)
            return RawChild(node, visual.Formatter);

        var children = visual.Children!;
        var (ok, child, reason) = await _visualizers.CallAsync(visual.Registration, node, visual.Context, _options.VisualizerTimeout,
            token => children.ChildAt(index, token), ct).ConfigureAwait(false);
        if (!ok || child is null)
        {
            return new VariableInfo("[" + index + "]", Messages.VisualizerChildFailed(reason ?? Messages.VisualizerReturnedNothing), "",
                ValueKind.Error, null, 0, false, null, null);
        }

        var info = await ToInfoAsync(child.Node.Renamed(child.Name, child.EvaluateName),
            _formatter, expandable: true, raw: false, immediate: false, ct).ConfigureAwait(false);
        // A row without an expression cannot be modified.
        return child.EvaluateName is null ? info with { CanModify = false } : info;
    }

    private async Task<ValueNode> PlainChildAsync(ValueNode node, int index, CancellationToken ct) =>
        (await _decoder!.GetChildrenAsync(node, index, 1, ct).ConfigureAwait(false))[0];

    /// <summary>DBG-506: the plain summary and type, the plain children decoded in raw mode, the expression with <c>,r</c>, not modifiable.</summary>
    private VariableInfo RawChild(ValueNode node, ValueFormatter formatter) =>
        new(RawChildName, formatter.Format(node), node.TypeName, node.Kind,
            node.ChildCount > 0 ? NewHandle(new Handle { Node = node, Raw = true }) : null, node.ChildCount, false,
            node.EvaluateName is { } evaluateName ? evaluateName + ",r" : null, node.Address);

    // ---- expressions (§9) ----

    private Evaluator NewEvaluator(FrameContext frame) =>
        new(_pdi ?? throw Evaluator.Error(Messages.NoTypeInfo), _debuggee, frame);

    private LoweredExpression Parse(string expression, bool allowFormatSuffix) =>
        SubsetValidator.Parse(expression, name => _pdi is not null && Evaluator.FindType(_pdi, name) is not null, allowFormatSuffix);

    /// <summary>
    /// Parses and evaluates an expression in a frame and presents the result (§9.6). Syntax errors fault with
    /// <c>ExpressionSyntax</c>, everything else with <c>ExpressionEvaluation</c> (DBG-412).
    /// </summary>
    /// <param name="expression">Expression text; with <paramref name="allowFormatSuffix"/> it may end in a format suffix.</param>
    /// <param name="frame">Frame to evaluate in.</param>
    /// <param name="allowFormatSuffix">Watches and the evaluate dialog: yes; conditions: no.</param>
    /// <param name="expandable">False when the result is only rendered (no handle is made).</param>
    /// <param name="ct">Cancellation.</param>
    public async Task<VariableInfo> EvaluateAsync(string expression, FrameContext frame, bool allowFormatSuffix, bool expandable, CancellationToken ct)
    {
        var lowered = Parse(expression, allowFormatSuffix);
        if (lowered.Format == FormatSpec.Count)
            throw Evaluator.Error(Messages.FormatSuffixNotSupported(lowered.Count));
        var value = await NewEvaluator(frame).EvaluateAsync(lowered.Tree, ct).ConfigureAwait(false);
        bool? hex = lowered.Format switch { FormatSpec.Hex => true, FormatSpec.Decimal => false, _ => null };
        return await PresentAsync(value, expression.Trim(), lowered.Tree.ToPascal(), hex, expandable, ct, raw: lowered.Format == FormatSpec.Raw)
            .ConfigureAwait(false);
    }

    /// <summary>A breakpoint condition (DBG-210): must yield Boolean.</summary>
    public async Task<bool> EvaluateConditionAsync(string condition, FrameContext frame, CancellationToken ct)
    {
        var lowered = Parse(condition, allowFormatSuffix: false);
        return await NewEvaluator(frame).EvaluateValueAsync(lowered.Tree, ct).ConfigureAwait(false) is BoolValue result
            ? result.Flag
            : throw Evaluator.Error(Messages.BooleanExpected);
    }

    /// <summary>The text an expression contributes to a log-point message (DBG-213): its summary, a string without quotes.</summary>
    public async Task<string> EvaluateLogTextAsync(string expression, FrameContext frame, CancellationToken ct)
    {
        var lowered = Parse(expression, allowFormatSuffix: false);
        var evaluator = NewEvaluator(frame);
        var value = await evaluator.EvaluateAsync(lowered.Tree, ct).ConfigureAwait(false);
        if (value is StrValue or LocationValue { Type.Kind: PdiTypeKind.String }
            && await evaluator.ReadAsync(value, ct).ConfigureAwait(false) is StrValue text)
            return text.Text;
        return (await PresentAsync(value, expression, expression, null, expandable: false, ct).ConfigureAwait(false)).Value;
    }

    /// <summary>
    /// The storage an assignment target names (DBG-177 step 1) and the root variable it is, if it is one: faults with
    /// <c>NotModifiable</c> unless the expression is a location of a modifiable kind.
    /// </summary>
    public async Task<(LocationValue Location, string? RootName, bool IsGlobal, string EvaluateName)> ResolveTargetAsync(
        string evaluateName, FrameContext frame, CancellationToken ct)
    {
        var lowered = Parse(evaluateName, allowFormatSuffix: false);
        if (await NewEvaluator(frame).EvaluateAsync(lowered.Tree, ct).ConfigureAwait(false) is not LocationValue location
            || !Evaluator.IsModifiable(location.Type))
            throw new DebuggerException(DebugErrorCode.NotModifiable, Messages.NotModifiable(evaluateName.Trim()));
        string? root = null;
        var isGlobal = false;
        if (lowered.Tree is Ast.Name name)
        {
            root = Evaluator.FindVariable(frame, name.Identifier)?.Name;
            if (root is null && Evaluator.FindGlobal(frame, name.Identifier) is { } global)
                (root, isGlobal) = (global.Name, true);
        }

        return (location, root, isGlobal, lowered.Tree.ToPascal());
    }

    /// <summary>The storage bytes of the value of <paramref name="valueExpression"/> converted to the target's type (DBG-177 steps 2 and 3).</summary>
    public async Task<byte[]> EvaluateForAssignmentAsync(string valueExpression, PdiType target, FrameContext frame, CancellationToken ct)
    {
        var lowered = Parse(valueExpression, allowFormatSuffix: false);
        return Evaluator.ConvertForAssignment(await NewEvaluator(frame).EvaluateValueAsync(lowered.Tree, ct).ConfigureAwait(false), target);
    }

    // ---- expression results (§9.6) ----

    /// <summary>
    /// The result node of an expression: a location is a full node (expandable, modifiable per DBG-176, with
    /// <paramref name="evaluateName"/>); an immediate is a leaf.
    /// </summary>
    /// <param name="value">What the expression evaluated to.</param>
    /// <param name="name">Name of the node: the expression as typed.</param>
    /// <param name="evaluateName">Normalised expression text.</param>
    /// <param name="hexIntegers">Format suffix: integers of this result in hexadecimal (true) or decimal (false); null: the session's option.</param>
    /// <param name="expandable">False when no handle may be made (the result is only rendered, e.g. for a log point).</param>
    /// <param name="ct">Cancellation.</param>
    /// <param name="raw">The raw suffix: no visualiser for the result or below it (DBG-508).</param>
    public async Task<VariableInfo> PresentAsync(
        Value value, string name, string evaluateName, bool? hexIntegers, bool expandable, CancellationToken ct, bool raw = false)
    {
        var formatter = hexIntegers is { } hex && hex != _options.HexIntegers
            ? new ValueFormatter(_options with { HexIntegers = hex })
            : _formatter;
        if (value is LocationValue location)
        {
            var node = await _decoder!.DecodeAsync(name, location.Type, location.Address, evaluateName, ct, location.OpenArrayHigh).ConfigureAwait(false);
            return await ToInfoAsync(node, formatter, expandable, raw, immediate: false, ct).ConfigureAwait(false);
        }

        var info = await ToInfoAsync(Immediate(value, name), formatter, expandable, raw, immediate: true, ct).ConfigureAwait(false);
        return info with
        {
            TypeName = value is NilValue ? SyntheticTypes.Pointer.Name : value.TypeName, EvaluateName = null, Address = null, CanModify = false,
        };
    }

    /// <summary>An immediate value as a node, so that it is rendered by the same rules as a value read from memory.</summary>
    private static ValueNode Immediate(Value value, string name)
    {
        ValueNode Node(ValueKind kind, PdiType type, byte[] bytes, ulong pointer = 0, string? text = null) => new()
        {
            Name = name, Kind = kind, Type = type, TypeName = ValueFormatter.TypeName(type), Bytes = bytes, Pointer = pointer,
            Text = text, Length = text?.Length ?? 0,
        };

        static byte[] Bytes(ulong bits, int size)
        {
            var bytes = new byte[8];
            BinaryPrimitives.WriteUInt64LittleEndian(bytes, bits);
            return bytes[..Math.Clamp(size, 1, 8)];
        }

        switch (value)
        {
            case IntValue i:
            {
                var type = i.FitsInt32 ? SyntheticTypes.Integer : i.Unsigned ? SyntheticTypes.UInt64 : SyntheticTypes.Int64;
                return Node(ValueKind.Integer, type, Bytes(unchecked((ulong)i.Bits), type.Size));
            }

            case FloatValue f:
                return Node(ValueKind.Float, SyntheticTypes.Double, Bytes(BitConverter.DoubleToUInt64Bits(f.Number), 8));
            case BoolValue b:
                return Node(ValueKind.Boolean, SyntheticTypes.Boolean, [b.Flag ? (byte)1 : (byte)0]);
            case CharValue c:
                return Node(ValueKind.Char, SyntheticTypes.Char, Bytes(c.Unit, 2));
            case StrValue s:
                return Node(ValueKind.String, SyntheticTypes.String, [], text: s.Text);
            case EnumValue e:
                return Node(ValueKind.Enum, e.Type, Bytes(unchecked((ulong)e.Ordinal), e.Type.Size));
            case SetValue s:
            {
                var bytes = new byte[32];
                s.Members.TryWriteBytes(bytes, out _, isUnsigned: true);
                return Node(ValueKind.Set, SyntheticTypes.SetOf(s.Element), bytes);
            }

            case PtrValue p:
                return Node(ValueKind.Pointer, p.Target is null ? SyntheticTypes.Pointer : SyntheticTypes.PointerTo(p.Target), Bytes(p.Address, 8), p.Address);
            case InstanceValue i:
                return Node(ValueKind.ClassInstance, i.Type, Bytes(i.Address, 8), i.Address, i.ClassName);
            case NilValue:
                return Node(ValueKind.Pointer, SyntheticTypes.Pointer, new byte[8]);
            case TypeValue t:
                throw Evaluator.Error(Messages.TypeIsNotValue(t.TypeName));
            default:
                throw Evaluator.Error(Messages.NotAvailable);
        }
    }
}
