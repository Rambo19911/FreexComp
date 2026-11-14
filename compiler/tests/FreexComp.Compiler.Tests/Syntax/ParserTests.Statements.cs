using FreexComp.Compiler.Syntax;

namespace FreexComp.Compiler.Tests.Syntax;

/// <summary>grammar.md §6: every statement form, dangling else and LANG-165…167 (C-017).</summary>
public sealed partial class ParserTests
{
    /// <summary>
    /// The statements of <c>begin &lt;body&gt; end.</c> (which must parse cleanly) as S-expressions, separators shown as
    /// <c>;</c>.
    /// </summary>
    private static string DumpBody(string body)
    {
        var text = "begin " + body + " end.";
        var tree = Parse(text);
        Assert.Equal(text, tree.Root.ToFullString());
        Assert.Empty(tree.Diagnostics);
        var statements = ((ProgramSyntax)tree.Root).Block.Body.Statements;
        return string.Join(" ", statements.GetWithSeparators().Select(item => item.AsNode() is { } node ? SyntaxDump.Of(node) : item.AsToken().Text));
    }

    private static string Call(string name) => $"(ExpressionStatement (IdentifierName {name}))";

    // ------------------------------------------------------------------ simple statements

    [Fact]
    public void Statements_AssignmentsAndCalls()
    {
        Assert.Equal(
            "(AssignmentStatement (IdentifierName x) := (LiteralExpression 1)) ; "
            + "(AssignmentStatement (DereferenceExpression (ElementAccessExpression (MemberAccessExpression (IdentifierName a) . (IdentifierName b)) "
            + "[ (IdentifierName i) ]) ^) := (InvocationExpression (IdentifierName f) (ArgumentList ( (Argument (IdentifierName x)) )))) ; "
            + $"{Call("Foo")} ; "
            + "(ExpressionStatement (InvocationExpression (IdentifierName Foo) (ArgumentList ( (Argument (LiteralExpression 1)) , (Argument (LiteralExpression 2)) )))) ; "
            + "(ExpressionStatement (MemberAccessExpression (IdentifierName Obj) . (IdentifierName Method))) ; "
            + "(AssignmentStatement (MemberAccessExpression (InvocationExpression (IdentifierName TFoo) (ArgumentList ( (Argument (IdentifierName o)) ))) "
            + ". (IdentifierName P)) := (IdentifierName y))",
            DumpBody("x := 1; a.b[i]^ := f(x); Foo; Foo(1, 2); Obj.Method; TFoo(o).P := y"));
    }

    /// <summary>C-206: a statement starting with the keyword type name <c>string</c> in a type cast is an assignment target (LANG-361).</summary>
    [Fact]
    public void KeywordTypeCastAsAssignmentTarget()
    {
        Assert.Equal(
            "(AssignmentStatement (StringCastExpression string ( (IdentifierName S) )) := (IdentifierName T)) ; "
            + "(AssignmentStatement (ElementAccessExpression (StringCastExpression string ( (IdentifierName P) )) [ (LiteralExpression 1) ]) "
            + ":= (LiteralExpression 'x'))",
            DumpBody("string(S) := T; string(P)[1] := 'x'"));
    }

    [Fact]
    public void Statements_Inherited()
    {
        Assert.Equal(
            "(InheritedStatement (InheritedExpression inherited)) ; "
            + "(InheritedStatement (InvocationExpression (InheritedExpression inherited (IdentifierName Create)) "
            + "(ArgumentList ( (Argument (LiteralExpression 1)) )))) ; "
            + "(InheritedStatement (InheritedExpression inherited (IdentifierName Destroy))) ; "
            + "(ExpressionStatement (MemberAccessExpression (InheritedExpression inherited (IdentifierName Foo)) . (IdentifierName Bar)))",
            DumpBody("inherited; inherited Create(1); inherited Destroy; inherited Foo.Bar"));
    }

    [Fact]
    public void Statements_GotoAndLabels()
    {
        Assert.Equal(
            "(GotoStatement goto (IdentifierName L)) ; "
            + "(LabeledStatement (IdentifierName L) : (AssignmentStatement (IdentifierName x) := (LiteralExpression 1))) ; "
            + "(LabeledStatement (LiteralExpression 10) : (EmptyStatement)) ; "
            + "(GotoStatement goto (LiteralExpression 10))",
            DumpBody("goto L; L: x := 1; 10: ; goto 10"));
    }

    [Fact]
    [Trait("Req", "LANG-167")]
    public void Statements_Raise()
    {
        Assert.Equal(
            "(RaiseStatement raise) ; (RaiseStatement raise (IdentifierName E)) ; "
            + "(RaiseStatement raise (InvocationExpression (MemberAccessExpression (IdentifierName E) . (IdentifierName Create)) "
            + "(ArgumentList ( (Argument (LiteralExpression 'x')) ))) at (IdentifierName Addr))",
            DumpBody("raise; raise E; raise E.Create('x') at Addr"));

        var text = "begin raise E at P end.";
        var raise = (RaiseStatementSyntax)((ProgramSyntax)Parse(text).Root).Block.Body.Statements[0];
        Assert.Equal(SyntaxKind.AtKeyword, raise.AtKeyword!.Value.ContextualKind);
        Assert.Equal(SyntaxKind.IdentifierToken, raise.AtKeyword!.Value.Kind);
    }

    [Fact]
    public void Statements_InlineVarAndConst()
    {
        Assert.Equal(
            "(InlineVarStatement var (IdentifierName x) : (NamedType (IdentifierName Integer)) := (LiteralExpression 5)) ; "
            + "(InlineVarStatement var (IdentifierName y) := (LiteralExpression 'a')) ; "
            + "(InlineVarStatement var (IdentifierName a) , (IdentifierName b) : (NamedType (IdentifierName Real))) ; "
            + "(InlineConstStatement const C = (LiteralExpression 3)) ; "
            + "(InlineConstStatement const D : (NamedType (IdentifierName Integer)) = (LiteralExpression 4))",
            DumpBody("var x: Integer := 5; var y := 'a'; var a, b: Real; const C = 3; const D: Integer = 4"));
    }

    // ------------------------------------------------------------------ if (LANG-165)

    [Fact]
    public void Statements_IfThenElse()
    {
        Assert.Equal(
            "(IfStatement if (IdentifierName a) then (AssignmentStatement (IdentifierName x) := (LiteralExpression 1)) else "
            + $"(IfStatement if (IdentifierName b) then {Call("Foo")} else (EmptyStatement))) ; "
            + "(IfStatement if (IdentifierName c) then (CompoundStatement begin end)) ; "
            + $"(IfStatement if (IdentifierName d) then (EmptyStatement) else {Call("y")})",
            DumpBody("if a then x := 1 else if b then Foo else ; if c then begin end; if d then else y"));
    }

    [Fact]
    [Trait("Req", "LANG-165")]
    public void Statements_DanglingElse_BindsToTheNearestIf()
    {
        Assert.Equal(
            $"(IfStatement if (IdentifierName a) then (IfStatement if (IdentifierName b) then {Call("x")} else {Call("y")}))",
            DumpBody("if a then if b then x else y"));
    }

    [Fact]
    [Trait("Req", "LANG-165")]
    public void Statements_SemicolonBeforeElse_IsE1105AndTheElseStillBelongsToTheIf()
    {
        var text = "begin if a then x; else y end.";

        Assert.Equal(["E1105(1,18): ';' not allowed before 'else'"], Diagnostics(text));
        Assert.Equal(
            $"(Program (Block (CompoundStatement begin (IfStatement if (IdentifierName a) then {Call("x")} else {Call("y")}) end)) . <eof>)",
            Dump(text, false));
    }

    // ------------------------------------------------------------------ case

    [Fact]
    public void Statements_Case()
    {
        Assert.Equal(
            $"(CaseStatement case (IdentifierName x) of (CaseArm (CaseLabel (LiteralExpression 1)) : {Call("Foo")}) ; "
            + "(CaseArm (CaseLabel (LiteralExpression 2)) , (CaseLabel (LiteralExpression 3) .. (LiteralExpression 5)) : (CompoundStatement begin end)) ; end) ; "
            + "(CaseStatement case (IdentifierName y) of (CaseArm (CaseLabel (LiteralExpression 'a')) : (EmptyStatement)) ; "
            + $"else {Call("Bar")} ; {Call("Baz")} end) ; "
            + $"(CaseStatement case (IdentifierName z) of (CaseArm (CaseLabel (UnaryExpression - (LiteralExpression 1))) : {Call("Foo")}) else end)",
            DumpBody("case x of 1: Foo; 2, 3..5: begin end; end; case y of 'a': ; else Bar; Baz end; case z of -1: Foo else end"));
    }

    [Fact]
    [Trait("Req", "LANG-165")]
    public void Statements_CaseArmIf_SemicolonBeforeElseEndsTheArm()
    {
        Assert.Equal(
            "(CaseStatement case (IdentifierName x) of (CaseArm (CaseLabel (LiteralExpression 1)) : "
            + $"(IfStatement if (IdentifierName a) then {Call("Foo")})) ; else {Call("Bar")} end)",
            DumpBody("case x of 1: if a then Foo; else Bar end"));
        Assert.Equal(
            "(CaseStatement case (IdentifierName x) of (CaseArm (CaseLabel (LiteralExpression 1)) : "
            + $"(IfStatement if (IdentifierName a) then {Call("Foo")} else {Call("Bar")})) end)",
            DumpBody("case x of 1: if a then Foo else Bar end"));
        Assert.Equal(
            ["E1105(1,39): ';' not allowed before 'else'"],
            Diagnostics("begin case x of 1: begin if a then Foo; else Bar end end end."));
    }

    // ------------------------------------------------------------------ loops and with

    [Fact]
    public void Statements_WhileAndRepeat()
    {
        Assert.Equal(
            "(WhileStatement while (IdentifierName a) do (AssignmentStatement (IdentifierName x) := (LiteralExpression 1))) ; "
            + $"(RepeatStatement repeat {Call("Foo")} ; {Call("Bar")} until (BinaryExpression (IdentifierName a) and (IdentifierName b))) ; "
            + "(RepeatStatement repeat until (IdentifierName c))",
            DumpBody("while a do x := 1; repeat Foo; Bar until a and b; repeat until c"));
    }

    [Fact]
    public void Statements_ForToDownToAndIn()
    {
        Assert.Equal(
            "(ForStatement for (IdentifierName i) := (LiteralExpression 1) to (LiteralExpression 10) do (EmptyStatement)) ; "
            + $"(ForStatement for (IdentifierName i) := (IdentifierName n) downto (LiteralExpression 0) do {Call("Foo")}) ; "
            + "(ForStatement for var (IdentifierName j) : (NamedType (IdentifierName Integer)) := (LiteralExpression 0) to (LiteralExpression 1) do (EmptyStatement)) ; "
            + $"(ForInStatement for (IdentifierName c) in (IdentifierName s) do {Call("Foo")}) ; "
            + $"(ForInStatement for var (IdentifierName e) in (IdentifierName List) do {Call("Foo")}) ; "
            + "(ForInStatement for var (IdentifierName k) : (NamedType (IdentifierName Char)) in (BinaryExpression (IdentifierName s) + (IdentifierName t)) do (EmptyStatement))",
            DumpBody("for i := 1 to 10 do ; for i := n downto 0 do Foo; for var j: Integer := 0 to 1 do ; "
                + "for c in s do Foo; for var e in List do Foo; for var k: Char in s + t do"));
    }

    [Fact]
    public void Statements_With()
    {
        Assert.Equal(
            $"(WithStatement with (IdentifierName a) , (MemberAccessExpression (IdentifierName b) . (IdentifierName c)) do {Call("Foo")})",
            DumpBody("with a, b.c do Foo"));
    }

    // ------------------------------------------------------------------ try (LANG-166)

    [Fact]
    public void Statements_TryFinallyAndTryExcept()
    {
        Assert.Equal(
            $"(TryFinallyStatement try {Call("Foo")} finally {Call("Bar")} ; {Call("Baz")} end) ; "
            + $"(TryExceptStatement try {Call("Foo")} except {Call("Bar")} end) ; "
            + "(TryExceptStatement try except end)",
            DumpBody("try Foo finally Bar; Baz end; try Foo except Bar end; try except end"));
    }

    [Fact]
    [Trait("Req", "LANG-166")]
    public void Statements_ExceptionHandlers()
    {
        Assert.Equal(
            $"(TryExceptStatement try {Call("Foo")} except "
            + $"(ExceptionHandler on E : (NamedType (IdentifierName EError)) do {Call("Bar")}) ; "
            + "(ExceptionHandler on (NamedType (QualifiedName (IdentifierName Sys) . (IdentifierName EOther))) do (EmptyStatement)) ; "
            + $"else {Call("Baz")} end)",
            DumpBody("try Foo except on E: EError do Bar; on Sys.EOther do ; else Baz end"));
        Assert.Equal(
            "(TryExceptStatement try except "
            + $"(ExceptionHandler on E : (NamedType (IdentifierName X)) do (IfStatement if (IdentifierName a) then {Call("Foo")})) ; "
            + $"else {Call("Bar")} end)",
            DumpBody("try except on E: X do if a then Foo; else Bar end"));

        var handler = ((TryExceptStatementSyntax)((ProgramSyntax)Parse("begin try except on E: X do end end.").Root).Block.Body.Statements[0]).Handlers[0];
        Assert.Equal(SyntaxKind.OnKeyword, handler.OnKeyword.ContextualKind);
        Assert.Equal("E", handler.Identifier!.Value.Text);
    }

    [Fact]
    [Trait("Req", "LANG-166")]
    public void Statements_OnNotFollowedByAnIdentifier_IsAPlainStatementList()
    {
        Assert.Equal(
            $"(TryExceptStatement try {Call("Foo")} except (AssignmentStatement (IdentifierName on) := (LiteralExpression 1)) end)",
            DumpBody("try Foo except on := 1 end"));
    }

    // ------------------------------------------------------------------ errors

    [Theory]
    [InlineData("begin x = 1 end.", "E1101(1,9): ';' expected but '=' found")]
    [InlineData("begin if a x end.", "E1101(1,12): 'then' expected but 'x' found")]
    [InlineData("begin while a Foo end.", "E1101(1,15): 'do' expected but 'Foo' found")]
    [InlineData("begin for i 1 to 2 do end.", "E1101(1,13): ':=' expected but '1' found")]
    [InlineData("begin for i := 1 2 do end.", "E1101(1,18): 'to' expected but '2' found")]
    [InlineData("begin repeat x end.", "E1101(1,16): 'until' expected but 'end' found")]
    [InlineData("begin try x end end.", "E1101(1,13): 'finally' expected but 'end' found")]
    [InlineData("begin case x 1: end end.", "E1101(1,14): 'of' expected but '1' found")]
    [InlineData("begin goto ; end.", "E1117(1,12): Label expected")]
    [InlineData("begin x := ; end.", "E1103(1,12): Expression expected but ';' found")]
    [InlineData("begin with do x end.", "E1103(1,12): Expression expected but 'do' found")]
    public void Statements_Errors(string text, string expected)
    {
        Assert.Equal([expected], Diagnostics(text));
    }

    [Fact]
    [Trait("Req", "LANG-182")]
    public void Statements_UnexpectedTokens_AreSkippedToTheNextSynchronisationPoint()
    {
        Assert.Equal(["E1100(1,7): Syntax error: unexpected 'then'"], Diagnostics("begin then 1 2; x := 1 end."));
        Assert.Equal(["E1100(1,7): Syntax error: unexpected 'else'"], Diagnostics("begin else x end."));
    }

    [Theory]
    [Trait("Req", "CMP-220")]
    [InlineData("begin if then else else ; case of : ; else end end.")]
    [InlineData("begin for var := in do try on : do ; except on do end end")]
    [InlineData("begin repeat while do with , do raise at ; goto 1 : until")]
    public void Statements_RoundTrip(string text)
    {
        Assert.Equal(text, Parse(text).Root.ToFullString());
    }

    [Fact]
    [Trait("Req", "LANG-180")]
    public void Statements_RandomTokenSequences_NeverThrowAndRoundTrip()
    {
        string[] vocabulary =
        [
            "begin", "end", "if", "then", "else", "case", "of", "while", "do", "repeat", "until", "for", "to", "downto", "in",
            "with", "try", "finally", "except", "on", "raise", "at", "goto", "var", "const", "inherited", ":=", ":", ";", ",",
            ".", "..", "(", ")", "[", "]", "^", "@", "=", "+", "x", "1", "'s'", "string", "nil", "not",
        ];
        var random = new Random(4711);
        for (var i = 0; i < 1000; i++)
        {
            var text = "begin " + string.Concat(Enumerable.Range(0, random.Next(0, 40)).Select(_ => vocabulary[random.Next(vocabulary.Length)] + " "));
            var tree = Parse(text);
            Assert.Equal(text, tree.Root.ToFullString());
            Assert.Equal(SyntaxKind.EndOfFileToken, tree.Root.LastToken().Kind);
        }
    }
}
