using FreexComp.Debugger.Values.TypeInfo;

namespace FreexComp.Debugger.Values.Visualizers;

/// <summary>
/// What a visualiser may use for one visualised value (visualizers.md §3): the display options, counted memory reads
/// (DBG-509), decoding through the one decoding path, fields by name, the plain nested form and type lookup. It offers
/// no memory write and no evaluation with calls. It is open only during a call of the framework (<see cref="Begin"/> …
/// <see cref="End"/>); each call has its own read budget and token.
/// </summary>
internal sealed class VisualizerContext
{
    /// <summary>Memory reads one call may make through the context (DBG-509).</summary>
    public const int MaxReads = 64;

    /// <summary>Bytes one call may read through the context in total (DBG-509).</summary>
    public const long MaxReadBytes = 1 << 20;

    private readonly PdiFile _pdi;
    private readonly IDebuggeeAccess _memory;
    private readonly ValueFormatter _formatter;
    private readonly ValueDecoder _decoder;

    // Nested mode: the pointer levels below the visualised value each node handed out lies at (DBG-151, DBG-507).
    private readonly Dictionary<ValueNode, int> _levels = new(ReferenceEqualityComparer.Instance);
    private int _deepest;

    private bool _open;
    private CancellationToken _ct;
    private int _reads;
    private long _bytes;

    public VisualizerContext(PdiFile pdi, IDebuggeeAccess memory, ValueFormatOptions options, bool nested, ValueNode value)
    {
        _pdi = pdi;
        _memory = memory;
        Options = options;
        Nested = nested;
        _formatter = new ValueFormatter(options);
        _decoder = new ValueDecoder(pdi, new Counted(this), options);
        _levels[value] = 0;
    }

    /// <summary>The display options, with the result's format suffix applied.</summary>
    public ValueFormatOptions Options { get; }

    /// <summary>The value is rendered inside a parent's one-line summary (DBG-507).</summary>
    public bool Nested { get; }

    /// <summary>The layout constants of the type information.</summary>
    public PdiAbi Abi => _pdi.Abi;

    /// <summary>Why the current call is a failure even if the visualiser went on (budget, second pointer level); null: none.</summary>
    internal string? Failure { get; private set; }

    internal void Begin(CancellationToken ct)
    {
        _open = true;
        _ct = ct;
        _reads = 0;
        _bytes = 0;
        Failure = null;
    }

    internal void End() => _open = false;

    /// <summary>
    /// Exactly <paramref name="count"/> bytes at <paramref name="address"/>, null when any is unreadable. Counted: the
    /// call beyond <see cref="MaxReads"/> reads or <see cref="MaxReadBytes"/> bytes throws and fails the call.
    /// </summary>
    public Task<byte[]?> ReadMemoryAsync(ulong address, int count)
    {
        EnsureOpen();
        _reads++;
        _bytes += Math.Max(count, 0);
        if (_reads > MaxReads || _bytes > MaxReadBytes)
            throw Fail(Messages.VisualizerReadBudgetExceeded);
        return _memory.ReadMemoryAsync(address, count, _ct);
    }

    /// <summary>
    /// Debuggee memory for a text source (DBG-509: the text calls have their own limit, one chunk, and no budget):
    /// exactly <paramref name="count"/> bytes, null when any is unreadable. Usable after the call that made the source.
    /// </summary>
    public Task<byte[]?> ReadTextMemoryAsync(ulong address, int count, CancellationToken ct) => _memory.ReadMemoryAsync(address, count, ct);

    /// <summary>Decodes a value of <paramref name="type"/> at <paramref name="address"/> (an address read from memory: one pointer level further).</summary>
    public async Task<ValueNode> DecodeAsync(string name, PdiType type, ulong address, string? evaluateName)
    {
        EnsureOpen();
        var level = Deeper(_deepest + 1);
        var node = await _decoder.DecodeAsync(name, type, address, evaluateName, _ct).ConfigureAwait(false);
        _levels[node] = level;
        return node;
    }

    /// <summary>
    /// The field <paramref name="fieldName"/> (case-insensitive) of a record or of a class instance and its ancestors,
    /// decoded; the most derived one when a name occurs twice. A missing field fails the call.
    /// </summary>
    public async Task<ValueNode> FieldAsync(ValueNode node, string fieldName)
    {
        EnsureOpen();
        var instance = node.Kind == ValueKind.ClassInstance;
        var fields = instance ? node.Type.AllFields() : node.Type.Kind == PdiTypeKind.Record ? node.Type.Fields : [];
        var field = fields.LastOrDefault(f => f.Name.Equals(fieldName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(Messages.VisualizerNoField(fieldName, node.TypeName));
        var level = Deeper(_levels.GetValueOrDefault(node, 1) + (instance ? 1 : 0));
        var baseAddress = instance ? node.Pointer : node.Address ?? 0;
        var child = await _decoder.DecodeAsync(field.Name, field.Type, unchecked(baseAddress + (ulong)field.Offset),
            node.EvaluateName is null ? null : node.EvaluateName + "." + field.Name, _ct).ConfigureAwait(false);
        _levels[child] = level;
        return child;
    }

    /// <summary>The plain form of a value inside a summary (DBG-174).</summary>
    public string FormatNested(ValueNode node) => _formatter.FormatNested(node);

    /// <summary>A type of the type information by name (case-insensitive), of the unit when one is given; alias entries are skipped.</summary>
    public PdiType? FindType(string? unit, string name) =>
        _pdi.Types.FirstOrDefault(type => type.AliasOf is null && type.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
            && (unit is null || string.Equals(type.Unit, unit, StringComparison.OrdinalIgnoreCase)));

    /// <summary>In nested mode a second pointer level is refused (DBG-507).</summary>
    private int Deeper(int level)
    {
        if (Nested && level > 1)
            throw Fail(Messages.VisualizerSecondPointerLevel);
        _deepest = Math.Max(_deepest, level);
        return level;
    }

    private InvalidOperationException Fail(string reason)
    {
        Failure ??= reason;
        return new InvalidOperationException(reason);
    }

    private void EnsureOpen()
    {
        if (!_open)
            throw new InvalidOperationException(Messages.VisualizerCallEnded);
    }

    /// <summary>The decoder's memory: every read goes through the context's count.</summary>
    private sealed class Counted(VisualizerContext context) : IDebuggeeAccess
    {
        public Task<byte[]?> ReadMemoryAsync(ulong address, int count, CancellationToken ct) => context.ReadMemoryAsync(address, count);

        public Task<bool> WriteMemoryAsync(ulong address, ReadOnlyMemory<byte> data, CancellationToken ct) => throw new NotSupportedException();

        public Task<ulong?> AddressOfAsync(int frameId, string variablePath, CancellationToken ct) => throw new NotSupportedException();

        public Task<IReadOnlyList<RawVariable>> GetRawVariablesAsync(int frameId, ScopeKind scope, CancellationToken ct) => throw new NotSupportedException();
    }
}
