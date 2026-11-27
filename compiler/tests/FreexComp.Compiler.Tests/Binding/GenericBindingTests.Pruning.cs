using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Types;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>
/// C-417: pruning on type-query conditions (intrinsics.md LANG-977) — in a generic instance body the statement an <c>if</c> or
/// <c>case</c> on a type query does not take is not bound: no diagnostic, no instantiation, no false unused-symbol hint.
/// </summary>
public sealed partial class GenericBindingTests
{
    private static string[] Errors(BindingResult result) => [.. Format(result.Diagnostics).Where(d => !d.StartsWith('H'))];

    private static string InstanceBody(BindingResult result, string instance, string method) =>
        BoundDump.Of(result.Routines.Single(r => r.Routine.Name == method && r.Routine.ContainingSymbol!.Name == instance).Body);

    /// <summary>
    /// LANG-977 rule 3: a statement that is valid for one kind of type only stands in the branch of that kind; the instance of another
    /// kind does not bind it. With a plain constant condition (rule 7) the same statement is bound for every instance: E2710.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-977")]
    public void Pruned_StatementIsNotBoundInAnInstance()
    {
        const string source = """
            program P;
            type
              TThing = class
                procedure Touch;
              end;
              TOps<T> = class
                class procedure Drop(var V: T);
              end;
            procedure TThing.Touch;
            begin
            end;
            class procedure TOps<T>.Drop(var V: T);
            begin
              {IF}
                V.Touch
              else
                V := Default(T);
            end;
            var
              I: Integer;
              O: TThing;
            begin
              TOps<Integer>.Drop(I);
              TOps<TThing>.Drop(O);
            end.
            """;

        var result = BindWithTypeKinds(source.Replace("{IF}", "if GetTypeKind(T) = tkClass then"));
        Assert.Equal([], Format(result.Diagnostics));
        Assert.Equal("(block (pruned False:Boolean (:= V:Integer 0:Integer)))", InstanceBody(result, "TOps<Integer>", "Drop"));
        Assert.StartsWith("(block (pruned True:Boolean (call Touch", InstanceBody(result, "TOps<TThing>", "Drop"), StringComparison.Ordinal);

        // the control: the same statement behind a constant that is no type query (rule 7) is bound for Integer too
        var control = BindWithTypeKinds(source.Replace("{IF}", "if SizeOf(T) = 8 then"));
        var error = Assert.Single(Format(control.Diagnostics));
        Assert.StartsWith("E2710(15,5): In instantiation 'TOps<Integer>': ", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// LANG-977 rule 3: a pruned statement requests no instantiation — no E2704 for the constrained generic it names, no instance of
    /// it in the generic's instance list and none among the unit's generic uses (CMP-385: nothing to emit).
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-977")]
    public void Pruned_NoInstantiationFromAPrunedStatement()
    {
        const string source = """
            program P;
            type
              TThing = class
              end;
              TObjBox<T: class> = class
                class function Count: Integer;
              end;
              TUser<T> = class
                class function Use: Integer;
              end;
            class function TObjBox<T>.Count: Integer;
            begin
              Result := 1;
            end;
            class function TUser<T>.Use: Integer;
            begin
              {IF}
                Result := TObjBox<T>.Count
              else
                Result := 0;
            end;
            var
              N: Integer;
            begin
              N := TUser<Integer>.Use;
              N := TUser<TThing>.Use;
            end.
            """;

        var result = BindWithTypeKinds(source.Replace("{IF}", "if GetTypeKind(T) = tkClass then"));
        Assert.Equal([], Errors(result));
        var box = result.Unit.ImplementationMembers.OfType<GenericTypeSymbol>().Single(g => g.Name == "TObjBox");
        Assert.Equal(["TObjBox<TThing>"], box.Instances.Values.Select(i => i.Name));
        Assert.Equal(["TUser<Integer>", "TUser<TThing>", "TObjBox<TThing>"], result.Unit.UsedInstances.Select(i => i.Name));
        Assert.DoesNotContain(result.Routines, r => r.Routine.ContainingSymbol?.Name == "TObjBox<Integer>");

        var control = BindWithTypeKinds(source.Replace("{IF}", "if SizeOf(T) = 8 then"));
        var error = Assert.Single(Errors(control));
        Assert.StartsWith("E2710(18,23): In instantiation 'TUser<Integer>': Type argument 'Integer' does not satisfy constraint ", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// LANG-977 rule 1 for <c>case</c>: the arm whose labels contain the selector's value is kept, else the <c>else</c> list, else
    /// nothing; the labels of every arm are still checked (a duplicate is E2064).
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-977")]
    public void Pruned_CaseKeepsOneArm()
    {
        const string source = """
            program P;
            type
              TSel<T> = class
                class function Pick(const V: T): Integer;
                class function None(const V: T): Integer;
              end;
            class function TSel<T>.Pick(const V: T): Integer;
            begin
              case GetTypeKind(T) of
                tkInteger: Result := Integer(V) + 1;
                tkUString: Result := Length(string(V));
                tkFloat, {LABEL}: Result := -1;
              else
                Result := 0;
                Result := Result - 2;
              end;
            end;
            class function TSel<T>.None(const V: T): Integer;
            begin
              Result := 7;
              case GetTypeKind(T) of
                tkInteger: Result := Integer(V) + 1;
                tkUString..tkSString: Result := Length(string(V));
              end;
            end;
            var
              N: Integer;
            begin
              N := TSel<Integer>.Pick(1) + TSel<string>.Pick('ab') + TSel<Double>.Pick(1.5) + TSel<Boolean>.Pick(True);
              N := TSel<Integer>.None(1) + TSel<string>.None('ab') + TSel<Boolean>.None(True);
            end.
            """;

        var result = BindWithTypeKinds(source.Replace("{LABEL}", "tkInt64"));
        Assert.Equal([], Errors(result));
        Assert.Equal(
            "(block (pruned 1:TTypeKind [1, 9, 4, 16] (:= Result:Integer (Add:Integer (cast Integer V:Integer) 1:Integer))))",
            InstanceBody(result, "TSel<Integer>", "Pick"));
        Assert.Equal(
            "(block (pruned 9:TTypeKind [1, 9, 4, 16] (:= Result:Integer (Length:Integer V:string))))",
            InstanceBody(result, "TSel<string>", "Pick"));
        Assert.Equal("(block (pruned 4:TTypeKind [1, 9, 4, 16] (:= Result:Integer -1:Integer)))", InstanceBody(result, "TSel<Double>", "Pick"));
        Assert.Equal(
            "(block (pruned 3:TTypeKind [1, 9, 4, 16] (block (:= Result:Integer 0:Integer) (:= Result:Integer (Subtract:Integer Result:Integer 2:Integer)))))",
            InstanceBody(result, "TSel<Boolean>", "Pick"));
        Assert.Equal(
            "(block (:= Result:Integer 7:Integer) (pruned 9:TTypeKind [1, 9..11] (:= Result:Integer (Length:Integer V:string))))",
            InstanceBody(result, "TSel<string>", "None"));
        Assert.Equal("(block (:= Result:Integer 7:Integer) (pruned 3:TTypeKind [1, 9..11]))", InstanceBody(result, "TSel<Boolean>", "None"));    // no arm, no else: nothing

        // labels are bound for every arm, whichever is kept
        var duplicate = BindWithTypeKinds(source.Replace("{LABEL}", "tkInteger"));
        Assert.Equal(
            ["E2710(12,14): In instantiation 'TSel<Integer>': Duplicate case label 1"],
            Errors(duplicate).Where(e => e.Contains("'TSel<Integer>'", StringComparison.Ordinal)));
        Assert.Equal(4, Errors(duplicate).Length);                                // one per instance of Pick
    }

    /// <summary>
    /// LANG-977 definition (ii): a condition that names a true constant whose declared value depends on a type query is a type-query
    /// condition — a local constant, one made of another, a constant of the generic itself and an inline constant.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-977")]
    public void Pruned_ThroughATypeQueryConstant()
    {
        var result = BindWithTypeKinds("""
            program P;
            type
              TRaw<T> = class
              public
                const IsText = GetTypeKind(T) = tkUString;
                class function Size(const V: T): Integer;
                class function Text(const V: T): Integer;
              end;
            class function TRaw<T>.Size(const V: T): Integer;
            const
              Managed = IsManagedType(T);
              Raw = not Managed;
            begin
              if Raw then
                Result := Integer(V)
              else
                Result := Length(string(V));
            end;
            class function TRaw<T>.Text(const V: T): Integer;
            begin
              Result := 0;
              if IsText then
                Result := Length(string(V));
              const Number = GetTypeKind(T) in [tkInteger, tkInt64];
              if Number and (SizeOf(T) = 4) then
                Result := Integer(V);
            end;
            var
              N: Integer;
            begin
              N := TRaw<Integer>.Size(1) + TRaw<string>.Size('ab');
              N := TRaw<Integer>.Text(1) + TRaw<string>.Text('ab');
            end.
            """);

        Assert.Equal([], Errors(result));
        Assert.Equal("(block (pruned True:Boolean (:= Result:Integer (cast Integer V:Integer))))", InstanceBody(result, "TRaw<Integer>", "Size"));
        Assert.Equal(
            "(block (pruned False:Boolean (:= Result:Integer (Length:Integer V:string))))",
            InstanceBody(result, "TRaw<string>", "Size"));
        Assert.Equal(
            "(block (:= Result:Integer 0:Integer) (pruned False:Boolean) (locals  (block (pruned True:Boolean (:= Result:Integer (cast Integer V:Integer))))))",
            InstanceBody(result, "TRaw<Integer>", "Text"));
        Assert.Equal(
            "(block (:= Result:Integer 0:Integer) (pruned True:Boolean (:= Result:Integer (Length:Integer V:string))) (locals  (block (pruned False:Boolean))))",
            InstanceBody(result, "TRaw<string>", "Text"));
    }

    /// <summary>
    /// LANG-977 rule 5: a local read only in the pruned statement, one assigned only there, a parameter and a label used only there
    /// give no H2001, H2002, H2005 or E2073 in the instance that prunes it; a local used nowhere is still H2001. An outer local that
    /// only a pruned statement of a nested routine names counts as used too.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-977")]
    public void Pruned_NamesUsedOnlyThere_NoHints()
    {
        var result = BindWithTypeKinds("""
            program P;
            type
              TNames<T> = class
                class function Run(P: Integer): Integer;
                class function Nest: Integer;
              end;
            class function TNames<T>.Run(P: Integer): Integer;
            label
              Done;
            var
              ReadThere, AssignedThere, Nowhere: Integer;
            begin
              ReadThere := 1;
              Result := 0;
              if IsManagedType(T) then
              begin
                AssignedThere := ReadThere + P;
                Result := AssignedThere;
                goto Done;
                Result := -1;
              Done:
                Result := Result + 1;
              end;
            end;
            class function TNames<T>.Nest: Integer;
            var
              Outer: Integer;
              procedure Inner;
              begin
                if IsManagedType(T) then
                  Outer := 1;
              end;
            begin
              Inner;
              Result := 0;
            end;
            var
              N: Integer;
            begin
              N := TNames<Integer>.Run(1) + TNames<Integer>.Nest;
            end.
            """);

        Assert.Equal(["H2001(11,29): Variable 'Nowhere' is declared but never used"], Format(result.Diagnostics));
        Assert.Equal(
            "(block (:= ReadThere:Integer 1:Integer) (:= Result:Integer 0:Integer) (pruned False:Boolean))",
            InstanceBody(result, "TNames<Integer>", "Run"));
    }

    /// <summary>
    /// LANG-977 rule 2 with LANG-380: the kept statement stands alone, so a <c>goto</c> in kept code cannot reach a label that only a
    /// pruned statement places (E2072, as for a jump into a <c>case</c> arm); a <c>goto</c> inside the pruned statement is not bound.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-977")]
    public void Pruned_GotoIntoAPrunedStatement_IsE2072()
    {
        var result = BindWithTypeKinds("""
            program P;
            type
              TJump<T> = class
                class function Run: Integer;
              end;
            class function TJump<T>.Run: Integer;
            label
              Inside;
            begin
              Result := 0;
              if Result = 0 then
                goto Inside;
              if IsManagedType(T) then
              begin
              Inside:
                Result := 1;
              end;
            end;
            var
              N: Integer;
            begin
              N := TJump<Integer>.Run + TJump<string>.Run;
            end.
            """);

        Assert.Equal(["E2710(12,5): In instantiation 'TJump<Integer>': 'goto' cannot jump into or out of this statement"], Errors(result));
    }

    /// <summary>
    /// LANG-977 rule 7 (guard): a constant condition without a type query, a condition that is not constant although it contains one,
    /// and the condition of a loop are no type-query conditions — every statement is bound for every instance.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-977")]
    public void NotPruned_PlainConstantOrNonConstant()
    {
        const string source = """
            program P;
            type
              TPlain<T> = class
                class function Run(const V: T; Flag: Boolean): Integer;
              end;
            class function TPlain<T>.Run(const V: T; Flag: Boolean): Integer;
            begin
              Result := 0;
              {IF}
                Result := Integer(V);
            end;
            var
              N: Integer;
            begin
              N := TPlain<string>.Run('ab', True);
            end.
            """;

        foreach (var condition in new[] { "if SizeOf(T) = 4 then", "if (GetTypeKind(T) = tkInteger) and Flag then", "while GetTypeKind(T) = tkInteger do" })
        {
            var result = BindWithTypeKinds(source.Replace("{IF}", condition));
            var error = Assert.Single(Errors(result));
            Assert.StartsWith("E2710(10,15): In instantiation 'TPlain<string>': ", error, StringComparison.Ordinal);
        }

        // the non-constant condition keeps its if statement with both parts (here: Integer(V) is valid for Integer)
        var bound = BindWithTypeKinds(source.Replace("{IF}", "if IsManagedType(T) or Flag then").Replace("<string>.Run('ab'", "<Integer>.Run(1"));
        Assert.Equal([], Errors(bound));
        Assert.Equal(
            "(block (:= Result:Integer 0:Integer) (if (ConditionalOr:Boolean False:Boolean Flag:Boolean) (:= Result:Integer (cast Integer V:Integer))))",
            InstanceBody(bound, "TPlain<Integer>", "Run"));
    }
}
