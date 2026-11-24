using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Text;
using FreexComp.Compiler.Text;

namespace FreexComp.Compiler.Syntax;

/// <summary>
/// State shared by every file of one preprocessing run (the main file and its includes): the define set and the switch
/// state carry across include boundaries (LANG-852, 860); diagnostics of all files are collected in order.
/// </summary>
internal sealed class PreprocessorContext
{
    /// <summary>
    /// LANG-870 / CMP-1010: the product group of predefined symbols, always defined and target-independent (ASSERTIONS follows the
    /// C switch, see <see cref="DirectiveProcessor"/>; CONSOLE comes with the application type). The target group is
    /// <see cref="TargetInfo.PredefinedSymbols"/>.
    /// </summary>
    internal static readonly ImmutableArray<string> ProductSymbols = ["OPC", "OPC_1", "UNICODE"];

    /// <summary>C-127: the main file starts with <c>unit</c> (set once its first token is read): <c>{$APPTYPE}</c> there is W1003.</summary>
    public bool IsUnitFile { get; set; }

    public PreprocessorContext(ParseOptions? options)
    {
        Options = options;
        Defines.UnionWith(ProductSymbols);
        Defines.UnionWith((options?.Target ?? TargetRegistry.Default).PredefinedSymbols);
        if (options is not null)
        {
            Defines.UnionWith(options.Defines);
        }
    }

    public ParseOptions? Options { get; }

    public HashSet<string> Defines { get; } = new(IdentifierComparer.Instance);

    public DirectiveState State { get; set; } = DirectiveState.Default;

    public List<Diagnostic> Diagnostics { get; } = [];

    /// <summary>Active tokens produced so far by every lexer of this context (only kept for a condition evaluator).</summary>
    public IList<SyntaxToken> Emitted { get; set; } = new List<SyntaxToken>();

    /// <summary>C-244: false when a <see cref="Preprocessor"/> fills <see cref="Emitted"/> with its own output (no second copy).</summary>
    public bool LexerRecords { get; set; } = true;

    /// <summary>The token being lexed while its trailing trivia is scanned (C-244: made a token only when a condition needs it).</summary>
    public (SyntaxKind Kind, string Text, object? Value, int Start, System.Collections.Immutable.ImmutableArray<SyntaxTrivia> Leading, DirectiveState State, SourceText File)? Pending { get; set; }

    /// <summary>The active tokens produced so far (the pending one included, without its trailing trivia).</summary>
    public List<SyntaxToken> PrecedingTokens()
    {
        var tokens = new List<SyntaxToken>(Emitted.Count + 1);
        tokens.AddRange(Emitted.Where(t => t.Kind != SyntaxKind.EndOfFileToken));
        if (Pending is { } p)
        {
            tokens.Add(new SyntaxToken(p.Kind, p.Text, p.Value, p.Start, p.Leading, [], p.State, p.File));
        }

        return tokens;
    }

    /// <summary>C-244: a <c>{$IF}</c>/<c>{$ELSEIF}</c> condition went to the condition evaluator (the tree depends on its scope).</summary>
    public bool EvaluatedCondition { get; set; }
}

/// <summary>
/// Directive processing for one file (CMP-111, directives.md): the conditional stack and regions of this file, plus
/// the shared defines and switch state (<see cref="PreprocessorContext"/>). <see cref="PascalLexer"/> hands it every
/// directive met in active text, asks <see cref="IsActive"/> whether the following text is disabled (LANG-864) and
/// stamps <see cref="State"/> on tokens. Its own warnings and hints follow <c>{$WARNINGS}</c>, <c>{$HINTS}</c> and
/// <c>{$WARN}</c> (CMP-505).
/// </summary>
internal sealed class DirectiveProcessor
{
    /// <summary>directives.md §5: accepted and ignored silently, whatever the argument.</summary>
    private static readonly FrozenSet<string> IgnoredDirectives = new[]
    {
        "OPTIMIZATION", "DEBUGINFO", "LOCALSYMBOLS", "REFERENCEINFO", "FORM", "MINSTACKSIZE", "MAXSTACKSIZE",
        "IMAGEBASE", "DESCRIPTION", "LIBPREFIX", "LIBSUFFIX", "LIBVERSION", "WEAKPACKAGEUNIT", "DENYPACKAGEUNIT",
        "DESIGNONLY", "RUNONLY", "IMPLICITBUILD", "HPPEMIT", "EXTERNALSYM", "NODEFINE", "NOINCLUDE", "OBJTYPENAME",
        "STACKFRAMES", "SAFEDIVIDE", "VARSTRINGCHECKS", "OPENSTRINGS", "EXTENSION", "INLINE", "EXCESSPRECISION",
        "HIGHCHARUNICODE", "TEXTBLOCK", "LINK",
    }.ToFrozenSet();

    /// <summary>directives.md §5: single-letter switches accepted and ignored silently (legacy / debug-info letters).</summary>
    private const string IgnoredLetters = "ODLYWUGPVNEFKS";

    private readonly PreprocessorContext _context;
    private readonly Stack<Frame> _frames = new();
    private readonly Stack<TextSpan> _regions = new();
    private readonly Action<string, TextSpan, object[], DiagnosticSeverity> _report;

    private readonly SourceText _source;

    public DirectiveProcessor(PreprocessorContext context, SourceText source, Action<string, TextSpan, object[], DiagnosticSeverity> report)
    {
        _context = context;
        _source = source;
        _report = report;
    }

    /// <summary>True when the text at the current position is in an active branch of this file.</summary>
    public bool IsActive => _frames.Count == 0 || _frames.Peek().BranchActive;

    /// <summary>The switch state at the current position (shared with includes).</summary>
    public DirectiveState State
    {
        get => _context.State;
        private set => _context.State = value;
    }

    /// <summary>
    /// <c>{$I file}</c> / <c>{$INCLUDE file}</c> (LANG-875); <c>{$I+}</c>, <c>{$I-}</c> and <c>{$I-,R+}</c> are the
    /// I/O-check switch.
    /// </summary>
    public static bool IsInclude(string name, string argument) =>
        name == "INCLUDE" || (name == "I" && argument.Length > 0 && argument[0] is not ('+' or '-'));

    /// <summary>The file name of an include directive: the argument, unquoted.</summary>
    public static string IncludeFileName(DirectiveTriviaInfo directive) => Unquote(directive.Argument);

    /// <summary>Directive names that open a conditional (used to track nesting inside disabled text).</summary>
    public static bool OpensConditional(string name) => name is "IFDEF" or "IFNDEF" or "IFOPT" or "IF";

    public static bool ClosesConditional(string name) => name is "ENDIF" or "IFEND";

    /// <summary>Directives that may end a run of disabled text at nesting depth 0.</summary>
    public static bool EndsDisabledText(string name) => name is "ELSE" or "ELSEIF" || ClosesConditional(name);

    public void Process(DirectiveTriviaInfo directive)
    {
        if (ProcessConditional(directive) || !IsActive)
        {
            return;
        }

        var name = directive.Name;
        var argument = directive.Argument;
        if (name.Length == 0)
        {
            Report("W1001", directive.Span, name);   // {$}, {$ R+}: no name right after '$'
            return;
        }

        switch (name)
        {
            case "DEFINE":
                if (SymbolName(directive) is { } defined)
                {
                    _context.Defines.Add(defined);
                }

                return;
            case "UNDEF":
                if (SymbolName(directive) is { } undefined)
                {
                    _context.Defines.Remove(undefined);
                }

                return;
            case "RANGECHECKS": OnOff(directive, v => State with { RangeChecks = v }); return;
            case "OVERFLOWCHECKS": OnOff(directive, v => State with { OverflowChecks = v }); return;
            case "BOOLEVAL": OnOff(directive, v => State with { BoolEval = v }); return;
            case "ASSERTIONS": OnOff(directive, v => State with { Assertions = v }); return;
            case "IOCHECKS": OnOff(directive, v => State with { IoChecks = v }); return;
            case "TYPEINFO": OnOff(directive, v => State with { TypeInfo = v }); return;
            case "POINTERMATH": OnOff(directive, v => State with { PointerMath = v }); return;
            case "SCOPEDENUMS": OnOff(directive, v => State with { ScopedEnums = v }); return;
            case "WARNINGS": OnOff(directive, v => State with { WarningsOn = v }); return;
            case "HINTS": OnOff(directive, v => State with { HintsOn = v }); return;
            case "LONGSTRINGS": OnOff(directive, v => Unsupported(directive, !v, "E1205")); return;
            case "WRITEABLECONST": OnOff(directive, v => Unsupported(directive, v, "W1002")); return;
            case "TYPEDADDRESS": OnOff(directive, v => Unsupported(directive, v, "W1002")); return;
            case "EXTENDEDSYNTAX": OnOff(directive, v => Unsupported(directive, !v, "W1002")); return;
            case "ZEROBASEDSTRINGS": OnOff(directive, v => Unsupported(directive, v, "E1205")); return;
            case "ALIGN":
                SetSize(directive, argument.ToUpperInvariant() switch { "ON" => "8", "OFF" => "1", _ => argument }, [1, 2, 4, 8],
                        v => State with { Align = v });
                return;
            case "MINENUMSIZE": SetSize(directive, argument, [1, 2, 4], v => State with { MinEnumSize = v }); return;
            case "APPTYPE": AppType(directive); return;
            case "WARN": Warn(directive); return;
            case "RTTI": Rtti(directive); return;
            case "MESSAGE": Message(directive); return;
            case "REGION": _regions.Push(directive.Span); return;
            case "ENDREGION":
                if (!_regions.TryPop(out _))
                {
                    Report("W1004", directive.Span);
                }

                return;
            case "RESOURCE": Report("H1010", directive.Span); return;
            case "INCLUDE" or "I" when IsInclude(name, argument):
                // Expansion is done by the Preprocessor stream (LANG-875); only the argument is checked here.
                if (IncludeFileName(directive).Length == 0)
                {
                    Report("E1206", directive.Span, argument);
                }

                return;
            case "PUSHOPT" or "POPOPT": Report("W1001", directive.Span, name); return;   // Phase 8+
        }

        if (IgnoredDirectives.Contains(name))
        {
            return;
        }

        if (TryShortSwitches(directive))
        {
            return;
        }

        switch (name)
        {
            case "R" when argument.Length > 0:
                Report("H1010", directive.Span);    // {$R file}: resource file
                return;
            case "L" when argument.Length > 0:
                return;                             // {$L file}: link (CMP-625)
        }

        Report("W1001", directive.Span, name);
    }

    /// <summary>End of file: open conditionals (E1200, LANG-863) and regions (W1004).</summary>
    public void Finish()
    {
        foreach (var frame in _frames.Reverse())
        {
            _report("E1200", frame.Open, [], DiagnosticSeverity.Error);
        }

        _frames.Clear();
        foreach (var region in _regions.Reverse())
        {
            Report("W1004", region);
        }

        _regions.Clear();
    }

    // ------------------------------------------------------------------ conditionals (C-008)

    /// <summary>Handles IF* / ELSE* / ENDIF / IFEND; false for every other directive.</summary>
    private bool ProcessConditional(DirectiveTriviaInfo directive)
    {
        var name = directive.Name;
        if (OpensConditional(name))
        {
            // In an inactive branch only nesting matters; the lexer never hands those over (they are disabled text).
            var parentActive = IsActive;
            var condition = parentActive && Evaluate(directive);
            _frames.Push(new Frame(directive.Span, parentActive) { Taken = condition, BranchActive = condition });
            return true;
        }

        switch (name)
        {
            case "ELSEIF":
                if (!_frames.TryPeek(out var elseIfFrame) || elseIfFrame.InElse)
                {
                    Unexpected(directive);
                    return true;
                }

                // evaluated only when no earlier branch was taken (LANG-862); see Evaluate
                elseIfFrame.BranchActive = elseIfFrame.ParentActive && !elseIfFrame.Taken && Evaluate(directive);
                elseIfFrame.Taken |= elseIfFrame.BranchActive;
                return true;
            case "ELSE":
                if (!_frames.TryPeek(out var elseFrame) || elseFrame.InElse)
                {
                    Unexpected(directive);
                    return true;
                }

                elseFrame.InElse = true;
                elseFrame.BranchActive = elseFrame.ParentActive && !elseFrame.Taken;
                elseFrame.Taken = true;
                return true;
            case "ENDIF" or "IFEND":
                if (!_frames.TryPop(out _))
                {
                    Unexpected(directive);
                }

                return true;
            default:
                return false;
        }
    }

    private bool IsDefined(string symbol) =>
        _context.Defines.Contains(symbol) || (IdentifierComparer.Instance.Equals(symbol, "ASSERTIONS") && State.Assertions);

    private bool Evaluate(DirectiveTriviaInfo directive) => directive.Name switch
    {
        "IFDEF" => SymbolName(directive) is { } a && IsDefined(a),
        "IFNDEF" => SymbolName(directive) is { } b && !IsDefined(b),
        "IFOPT" => EvaluateIfOpt(directive),

        _ => EvaluateIf(directive),                                             // IF, ELSEIF
    };

    /// <summary>
    /// LANG-862: the condition through the binder callback (<see cref="ParseOptions.ConditionEvaluator"/>); without
    /// one (pure syntax operations) it counts as True. A syntax error in the condition makes it False.
    /// </summary>
    private bool EvaluateIf(DirectiveTriviaInfo directive)
    {
        if (_context.Options?.ConditionEvaluator is not { } evaluator)
        {
            return true;
        }

        _context.EvaluatedCondition = true;
        if (directive.Argument.Length == 0)
        {
            Report("E1206", directive.Span, directive.Argument);
            return false;
        }

        // the argument re-lexed in a copy of the file where everything before it is blanked (line breaks kept), so
        // its tokens and diagnostics carry the file's own positions
        var text = _source.Text;
        var start = text.IndexOf(directive.Argument, directive.Span.Start + 2 + directive.Name.Length, StringComparison.Ordinal);
        var blanked = new char[start + directive.Argument.Length];
        for (var i = 0; i < start; i++)
        {
            blanked[i] = text[i] is '\r' or '\n' ? text[i] : ' ';
        }

        directive.Argument.CopyTo(0, blanked, start, directive.Argument.Length);
        var argumentText = SourceText.From(_source.Path, new string(blanked));
        var lexer = new PascalLexer(argumentText);
        var tokens = ImmutableArray.CreateBuilder<SyntaxToken>();
        SyntaxToken token;
        do
        {
            token = lexer.Lex();
            tokens.Add(token);
        }
        while (token.Kind != SyntaxKind.EndOfFileToken);

        var parser = new Parser(argumentText, tokens.ToImmutable());
        var condition = parser.ParseDirectiveCondition();
        var syntaxErrors = lexer.Diagnostics.Concat(parser.Diagnostics).ToList();
        if (syntaxErrors.Count > 0)
        {
            _context.Diagnostics.AddRange(syntaxErrors);
            return false;
        }

        return evaluator.Evaluate(new ConditionRequest(condition, _context.PrecedingTokens(), IsDefined, _context.Diagnostics.Add));
    }

    /// <summary>LANG-881: <c>R Q B C I M H J T X A</c>; H, J, T and X cannot change (unsupported settings).</summary>
    private bool EvaluateIfOpt(DirectiveTriviaInfo directive)
    {
        var argument = directive.Argument;
        bool? on = argument.Length == 2 && argument[1] is '+' or '-'
            ? char.ToUpperInvariant(argument[0]) switch
            {
                'R' => State.RangeChecks,
                'Q' => State.OverflowChecks,
                'B' => State.BoolEval,
                'C' => State.Assertions,
                'I' => State.IoChecks,
                'M' => State.TypeInfo,
                'H' => true,
                'J' => false,
                'T' => false,
                'X' => true,
                'A' => State.Align > 1,
                _ => null,
            }
            : null;
        if (on is { } value)
        {
            return value == (argument[1] == '+');
        }

        Report("E1206", directive.Span, argument);
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

        Report("E1206", directive.Span, argument);
        return null;
    }

    private void Unexpected(DirectiveTriviaInfo directive) => Report("E1201", directive.Span, directive.Name);

    // ------------------------------------------------------------------ switches (C-009)

    /// <summary>
    /// <c>{$R+}</c>, <c>{$R+,Q-}</c>, <c>{$A4}</c>, <c>{$Z2}</c> (LANG-850). False when the directive is not of this
    /// form (for example <c>{$R file}</c>), so the caller can treat it otherwise.
    /// </summary>
    private bool TryShortSwitches(DirectiveTriviaInfo directive)
    {
        var raw = directive.Name + directive.Argument;
        var items = raw.Split(',');
        if (!items.All(i => i.Length == 2 && char.IsAsciiLetter(i[0]) && (i[1] is '+' or '-' || char.IsAsciiDigit(i[1]))))
        {
            return false;
        }

        foreach (var item in items)
        {
            var letter = char.ToUpperInvariant(item[0]);
            var plus = item[1] == '+';
            if (char.IsAsciiDigit(item[1]))
            {
                var size = item[1] - '0';
                switch (letter)
                {
                    case 'A' when size is 1 or 2 or 4 or 8: State = State with { Align = size }; continue;
                    case 'Z' when size is 1 or 2 or 4: State = State with { MinEnumSize = size }; continue;
                    case 'A' or 'Z': Report("E1206", directive.Span, item); continue;
                    default: Report("W1001", directive.Span, item); continue;
                }
            }

            switch (letter)
            {
                case 'R': State = State with { RangeChecks = plus }; break;
                case 'Q': State = State with { OverflowChecks = plus }; break;
                case 'B': State = State with { BoolEval = plus }; break;
                case 'C': State = State with { Assertions = plus }; break;
                case 'I': State = State with { IoChecks = plus }; break;
                case 'M': State = State with { TypeInfo = plus }; break;
                case 'A': State = State with { Align = plus ? 8 : 1 }; break;
                case 'H': Unsupported(item, !plus, "E1205", directive.Span); break;
                case 'J' or 'T': Unsupported(item, plus, "W1002", directive.Span); break;
                case 'X': Unsupported(item, !plus, "W1002", directive.Span); break;
                default:
                    if (!IgnoredLetters.Contains(letter, StringComparison.Ordinal))
                    {
                        Report("W1001", directive.Span, item);
                    }

                    break;
            }
        }

        return true;
    }

    /// <summary>LANG-880: long-form switches take ON/OFF (case-insensitive); anything else is E1206.</summary>
    private void OnOff(DirectiveTriviaInfo directive, Func<bool, DirectiveState?> apply)
    {
        bool? value = directive.Argument.ToUpperInvariant() switch { "ON" => true, "OFF" => false, _ => null };
        if (value is null)
        {
            Report("E1206", directive.Span, directive.Argument);
            return;
        }

        if (apply(value.Value) is { } state)
        {
            State = state;
        }
    }

    private DirectiveState? Unsupported(DirectiveTriviaInfo directive, bool unsupported, string code)
    {
        Unsupported(directive.Name + " " + directive.Argument, unsupported, code, directive.Span);
        return null;
    }

    private void Unsupported(string text, bool unsupported, string code, TextSpan span)
    {
        if (unsupported)
        {
            Report(code, span, text);
        }
    }

    private void SetSize(DirectiveTriviaInfo directive, string argument, int[] allowed, Func<int, DirectiveState> apply)
    {
        if (int.TryParse(argument, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var size)
            && allowed.Contains(size))
        {
            State = apply(size);
            return;
        }

        Report("E1206", directive.Span, directive.Argument);
    }

    /// <summary><c>{$APPTYPE CONSOLE|GUI}</c> defines / undefines CONSOLE. W1003 outside the program file needs the parser.</summary>
    private void AppType(DirectiveTriviaInfo directive)
    {
        if (_context.IsUnitFile)
        {
            Report("W1003", directive.Span);                                    // C-127: only the program file sets the application type
        }

        switch (directive.Argument.ToUpperInvariant())
        {
            case "CONSOLE": _context.Defines.Add("CONSOLE"); break;
            case "GUI": _context.Defines.Remove("CONSOLE"); break;
            default: Report("E1206", directive.Span, directive.Argument); break;
        }
    }

    /// <summary><c>{$WARN id ON|OFF|ERROR}</c>: id = code or symbolic name (CMP-509); a name may cover several codes.</summary>
    private void Warn(DirectiveTriviaInfo directive)
    {
        var parts = directive.Argument.Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        WarnSetting? setting = parts.Length == 2
            ? parts[1].ToUpperInvariant() switch { "ON" => WarnSetting.On, "OFF" => WarnSetting.Off, "ERROR" => WarnSetting.Error, _ => null }
            : null;
        var codes = parts.Length == 2
            ? DiagnosticCatalog.All
                .Where(d => d.DefaultSeverity != DiagnosticSeverity.Error
                            && (string.Equals(d.Code, parts[0], StringComparison.OrdinalIgnoreCase)
                                || string.Equals(d.Name, parts[0], StringComparison.OrdinalIgnoreCase)))
                .Select(d => d.Code)
                .ToList()
            : [];
        if (setting is null || codes.Count == 0)
        {
            Report("E1206", directive.Span, directive.Argument);
            return;
        }

        State = State with { WarnOverrides = State.WarnOverrides.SetItems(codes.Select(c => KeyValuePair.Create(c, setting.Value))) };
    }

    /// <summary>LANG-882: BROAD / PUBLISHED / DEFAULT; anything else W1002, ignored.</summary>
    private void Rtti(DirectiveTriviaInfo directive)
    {
        switch (directive.Argument.ToUpperInvariant())
        {
            case "BROAD": State = State with { Rtti = "broad" }; break;
            case "PUBLISHED": State = State with { Rtti = "published" }; break;
            case "DEFAULT": State = State with { Rtti = null }; break;
            default: Report("W1002", directive.Span, directive.Name + " " + directive.Argument); break;
        }
    }

    /// <summary>LANG-865: <c>{$MESSAGE [HINT|WARN|ERROR|FATAL] 'text'}</c>; the text may also be unquoted.</summary>
    private void Message(DirectiveTriviaInfo directive)
    {
        var argument = directive.Argument;
        var code = "H1100";
        if (argument.Length > 0 && argument[0] != '\'')
        {
            var end = argument.IndexOfAny([' ', '\t']);
            var kind = (end < 0 ? argument : argument[..end]).ToUpperInvariant();
            string? kindCode = kind switch { "HINT" => "H1100", "WARN" => "W1100", "ERROR" => "E1210", "FATAL" => "E1211", _ => null };
            if (kindCode is not null)
            {
                code = kindCode;
                argument = end < 0 ? "" : argument[end..].TrimStart();
            }
            else if (end >= 0 && argument[end..].TrimStart().StartsWith('\''))
            {
                Report("E1206", directive.Span, directive.Argument);
                return;
            }
        }

        Report(code, directive.Span, Unquote(argument));
    }

    /// <summary><c>'It''s'</c> → <c>It's</c>; text that is not a single quoted string is returned unchanged.</summary>
    internal static string Unquote(string text)
    {
        if (text.Length < 2 || text[0] != '\'' || text[^1] != '\'')
        {
            return text;
        }

        var sb = new StringBuilder();
        for (var i = 1; i < text.Length - 1; i++)
        {
            if (text[i] == '\'')
            {
                if (text[i + 1] != '\'')
                {
                    return text;    // not one quoted string
                }

                i++;
            }

            sb.Append(text[i]);
        }

        return sb.ToString();
    }

    // ------------------------------------------------------------------ reporting with warning control (CMP-505)

    private void Report(string code, TextSpan span, params object[] args)
    {
        var severity = DiagnosticCatalog.Get(code).DefaultSeverity;
        if (severity != DiagnosticSeverity.Error)
        {
            if (State.WarnOverrides.TryGetValue(code, out var setting))
            {
                if (setting == WarnSetting.Off)
                {
                    return;
                }

                severity = setting == WarnSetting.Error ? DiagnosticSeverity.Error : severity;
            }
            else if ((severity == DiagnosticSeverity.Warning && !State.WarningsOn) || (severity == DiagnosticSeverity.Hint && !State.HintsOn))
            {
                return;
            }
        }

        _report(code, span, args, severity);
    }

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
