using FreexComp.Compiler.Text;

namespace FreexComp.Compiler.Syntax;

/// <summary>
/// Directive processing for one file (CMP-111, directives.md): the define set, the conditional stack and the
/// single-letter switch state used by <c>{$IFOPT}</c>. <see cref="PascalLexer"/> hands it every directive met in
/// active text and asks <see cref="IsActive"/> to decide whether the following text is disabled (LANG-864).
/// </summary>
internal sealed class Preprocessor
{
    /// <summary>LANG-870: always defined (ASSERTIONS follows the C switch, see <see cref="IsDefined"/>).</summary>
    private static readonly string[] Predefined = ["OPC", "OPC_1", "MSWINDOWS", "WINDOWS", "WIN64", "CPUX64", "CPU64BITS", "UNICODE"];

    private readonly HashSet<string> _defines = new(IdentifierComparer.Instance);
    private readonly Dictionary<char, bool> _switches = new()
    {
        // LANG-881 letters with their defaults (directives.md §5); A+ = alignment > 1 (default 8).
        ['R'] = false, ['Q'] = false, ['B'] = false, ['C'] = true, ['I'] = true, ['M'] = false,
        ['H'] = true, ['J'] = false, ['T'] = false, ['X'] = true, ['A'] = true,
    };

    private readonly Stack<Frame> _frames = new();
    private readonly Action<string, TextSpan, object[]> _report;

    public Preprocessor(ParseOptions? options, Action<string, TextSpan, object[]> report)
    {
        _report = report;
        _defines.UnionWith(Predefined);
        if (options is not null)
        {
            _defines.UnionWith(options.Defines);
        }
    }

    /// <summary>True when the text at the current position is in an active branch.</summary>
    public bool IsActive => _frames.Count == 0 || _frames.Peek().BranchActive;

    /// <summary>Directive names that open a conditional (used to track nesting inside disabled text).</summary>
    public static bool OpensConditional(string name) => name is "IFDEF" or "IFNDEF" or "IFOPT" or "IF";

    public static bool ClosesConditional(string name) => name is "ENDIF" or "IFEND";

    /// <summary>Directives that may end a run of disabled text at nesting depth 0.</summary>
    public static bool EndsDisabledText(string name) => name is "ELSE" or "ELSEIF" || ClosesConditional(name);

    public void Process(DirectiveTriviaInfo directive)
    {
        var name = directive.Name;
        if (OpensConditional(name))
        {
            // In an inactive branch only nesting matters; the lexer never hands those over (they are disabled text).
            var parentActive = IsActive;
            var condition = parentActive && Evaluate(directive);
            _frames.Push(new Frame(directive.Span, parentActive) { Taken = condition, BranchActive = condition });
            return;
        }

        switch (name)
        {
            case "ELSEIF":
                if (!_frames.TryPeek(out var elseIfFrame) || elseIfFrame.InElse)
                {
                    Unexpected(directive);
                    return;
                }

                // Without the binder an {$ELSEIF} condition counts as True (LANG-862); see Evaluate.
                elseIfFrame.BranchActive = elseIfFrame.ParentActive && !elseIfFrame.Taken;
                elseIfFrame.Taken |= elseIfFrame.BranchActive;
                return;
            case "ELSE":
                if (!_frames.TryPeek(out var elseFrame) || elseFrame.InElse)
                {
                    Unexpected(directive);
                    return;
                }

                elseFrame.InElse = true;
                elseFrame.BranchActive = elseFrame.ParentActive && !elseFrame.Taken;
                elseFrame.Taken = true;
                return;
            case "ENDIF" or "IFEND":
                if (_frames.Count == 0)
                {
                    Unexpected(directive);
                    return;
                }

                _frames.Pop();
                return;
        }

        if (!IsActive)
        {
            return;
        }

        switch (name)
        {
            case "DEFINE":
                if (SymbolName(directive) is { } defined)
                {
                    _defines.Add(defined);
                }

                return;
            case "UNDEF":
                if (SymbolName(directive) is { } undefined)
                {
                    _defines.Remove(undefined);
                }

                return;
            default:
                TryApplyShortSwitches(directive);
                return;
        }
    }

    /// <summary>End of file: every conditional still open is E1200 at its opening directive (LANG-863).</summary>
    public void Finish()
    {
        foreach (var frame in _frames.Reverse())
        {
            _report("E1200", frame.Open, []);
        }

        _frames.Clear();
    }

    private bool IsDefined(string symbol) =>
        _defines.Contains(symbol) || (IdentifierComparer.Instance.Equals(symbol, "ASSERTIONS") && _switches['C']);

    private bool Evaluate(DirectiveTriviaInfo directive) => directive.Name switch
    {
        "IFDEF" => SymbolName(directive) is { } a && IsDefined(a),
        "IFNDEF" => SymbolName(directive) is { } b && !IsDefined(b),
        "IFOPT" => EvaluateIfOpt(directive),

        // LANG-862: during pure syntax operations a {$IF} condition is treated as True; evaluation through the
        // binder callback is task C-032.
        _ => true,
    };

    private bool EvaluateIfOpt(DirectiveTriviaInfo directive)
    {
        var argument = directive.Argument;
        if (argument.Length == 2 && argument[1] is '+' or '-'
            && _switches.TryGetValue(char.ToUpperInvariant(argument[0]), out var state))
        {
            return state == (argument[1] == '+');
        }

        _report("E1206", directive.Span, [argument]);
        return false;
    }

    /// <summary>The conditional symbol of DEFINE / UNDEF / IFDEF / IFNDEF; E1206 when missing or not an identifier.</summary>
    private string? SymbolName(DirectiveTriviaInfo directive)
    {
        var argument = directive.Argument;
        var end = 0;
        while (end < argument.Length && !char.IsWhiteSpace(argument[end]))
        {
            end++;
        }

        var name = argument[..end];
        if (name.Length > 0 && (SyntaxFacts.IsValidIdentifier(name) || SyntaxFacts.IsReservedWord(name)))
        {
            return name;
        }

        _report("E1206", directive.Span, [argument]);
        return null;
    }

    /// <summary><c>{$R+}</c>, <c>{$R+,Q-}</c>: single-letter switches (LANG-850). Anything else is left alone.</summary>
    private void TryApplyShortSwitches(DirectiveTriviaInfo directive)
    {
        var items = (directive.Name + directive.Argument).Split(',');
        if (!items.All(i => i.Length == 2 && char.IsAsciiLetter(i[0]) && i[1] is '+' or '-'))
        {
            return;
        }

        foreach (var item in items)
        {
            var letter = char.ToUpperInvariant(item[0]);
            if (_switches.ContainsKey(letter))
            {
                _switches[letter] = item[1] == '+';
            }
        }
    }

    private void Unexpected(DirectiveTriviaInfo directive) => _report("E1201", directive.Span, [directive.Name]);

    private sealed class Frame(TextSpan open, bool parentActive)
    {
        public TextSpan Open { get; } = open;

        public bool ParentActive { get; } = parentActive;

        /// <summary>Some branch of this conditional has been active already.</summary>
        public bool Taken { get; set; }

        public bool BranchActive { get; set; }

        public bool InElse { get; set; }
    }
}
