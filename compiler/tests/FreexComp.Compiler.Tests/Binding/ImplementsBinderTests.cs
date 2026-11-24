using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Types;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>
/// C-302 (com-interfaces.md §5): <c>property P: T read … implements I1, I2</c> — one synthesised private method per delegated
/// interface method (LANG-690, CMP-1207), resolution clauses first (LANG-661), inherited delegation reused, checks of LANG-692.
/// </summary>
[Trait("Phase", "8")]
public sealed class ImplementsBinderTests
{
    private static ClassTypeSymbol Class(FreexComp.Compiler.Binding.BindingResult result, string name) =>
        result.Unit.ImplementationMembers.Concat(result.Unit.InterfaceMembers).OfType<ClassTypeSymbol>().Single(c => c.Name == name);

    private static string[] Table(ClassTypeSymbol type, string intf) =>
        [.. type.Interfaces.Single(i => i.Interface.Name == intf).Implementations.Select(m => m.Owner.Name + "." + m.Name)];

    [Fact]
    [Trait("Req", "LANG-690")]
    [Trait("Req", "CMP-1207")]
    public void SynthesisedMethodsPerInterface()
    {
        var (result, _) = Bind("""
            program P;
            type
              ILog = interface
                procedure Line(const S: string);
                function Count: Integer;
              end;
              IStoreBase = interface
                procedure Clear;
              end;
              IStore = interface(IStoreBase)
                procedure Put(Key: Integer; var Value: string);
                function Get(Key: Integer): string; overload;
                function Get(const Key: string): string; overload;
              end;
              TStoreImpl = class
                procedure Clear; virtual;
                procedure Put(Key: Integer; var Value: string);
                function Get(Key: Integer): string; overload;
                function Get(const Key: string): string; overload;
              end;
              TOuter = class(ILog, IStore)
              private
                FLog: ILog;
                FStore: TStoreImpl;
              public
                property Log: ILog read FLog implements ILog;
                property Store: TStoreImpl read FStore implements IStore;
              end;
            procedure TStoreImpl.Clear; begin end;
            procedure TStoreImpl.Put(Key: Integer; var Value: string); begin end;
            function TStoreImpl.Get(Key: Integer): string; begin Result := '' end;
            function TStoreImpl.Get(const Key: string): string; begin Result := Key end;
            begin
            end.
            """);
        Assert.Empty(Format(result.Diagnostics));
        var outer = Class(result, "TOuter");
        var synthesised = outer.Methods.Where(m => m.Name.StartsWith("$dlg.", StringComparison.Ordinal)).ToList();
        Assert.Equal(
            ["$dlg.P.ILog.Line", "$dlg.P.ILog.Count", "$dlg.P.IStoreBase.Clear", "$dlg.P.IStore.Put", "$dlg.P.IStore.Get", "$dlg.P.IStore.Get"],
            synthesised.Select(m => m.Name));
        Assert.All(synthesised, m =>
        {
            Assert.Equal(Visibility.Private, m.Visibility);
            Assert.False(m.IsDispatched);
            Assert.Equal(MethodKind.Ordinary, m.MethodKind);
            Assert.Empty(m.Locations);
        });

        // the exact signature of the interface method (modes, types, result)
        var put = synthesised.Single(m => m.Name == "$dlg.P.IStore.Put");
        Assert.Equal(["Integer", "string"], put.Parameters.Select(p => p.Type.Name));
        Assert.Equal([ParameterMode.Value, ParameterMode.Var], put.Parameters.Select(p => p.Mode));
        Assert.Null(put.ReturnType);
        Assert.Equal("string", synthesised.First(m => m.Name == "$dlg.P.IStore.Get").ReturnType!.Name);
        Assert.Equal("P.TOuter.$dlg.P.ILog.Line", synthesised[0].LinkName);
        Assert.NotEqual(synthesised[4].LinkName, synthesised[5].LinkName);          // the overloads keep their signature suffix

        // the method tables point at them (LANG-691: the hidden fields and tables stay those of the declaring class)
        Assert.Equal(["TOuter.$dlg.P.ILog.Line", "TOuter.$dlg.P.ILog.Count"], Table(outer, "ILog"));
        Assert.Equal(["TOuter.$dlg.P.IStoreBase.Clear", "TOuter.$dlg.P.IStore.Put", "TOuter.$dlg.P.IStore.Get", "TOuter.$dlg.P.IStore.Get"], Table(outer, "IStore"));

        // each has a bound body: one statement calling through the property
        foreach (var method in synthesised)
        {
            var body = Assert.Single(result.Routines, r => ReferenceEquals(r.Routine, method));
            Assert.Single(body.Body.Statements);
            Assert.Empty(body.Diagnostics);
        }
    }

    [Fact]
    [Trait("Req", "LANG-690")]
    [Trait("Req", "LANG-661")]
    public void ResolutionClauseWinsOverDelegation()
    {
        var (result, _) = Bind("""
            program P;
            type
              IFoo = interface
                procedure A;
                procedure B;
              end;
              TImpl = class
                procedure A;
                procedure B;
              end;
              TOuter = class(IFoo)
                FImpl: TImpl;
                procedure MyB;
                procedure A;
                procedure IFoo.B = MyB;
                property Impl: TImpl read FImpl implements IFoo;
              end;
            procedure TImpl.A; begin end;
            procedure TImpl.B; begin end;
            procedure TOuter.MyB; begin end;
            procedure TOuter.A; begin end;
            begin
            end.
            """);
        Assert.Empty(Format(result.Diagnostics));
        var outer = Class(result, "TOuter");

        // B is taken back by the clause; A is delegated although the class has a method A of its own (LANG-690)
        Assert.Equal(["TOuter.$dlg.P.IFoo.A", "TOuter.MyB"], Table(outer, "IFoo"));
        Assert.DoesNotContain(outer.Methods, m => m.Name == "$dlg.P.IFoo.B");
    }

    [Fact]
    [Trait("Req", "LANG-692")]
    public void InheritedDelegationReused()
    {
        var (result, _) = Bind("""
            program P;
            type
              IFoo = interface
                procedure A;
                procedure B;
              end;
              TBase = class(IFoo)
                FImpl: IFoo;
                property Impl: IFoo read FImpl implements IFoo;
              end;
              TSame = class(TBase, IFoo)
              end;
              TOwn = class(TBase, IFoo)
                procedure B;
              end;
              TAgain = class(TBase, IFoo)
                FOther: IFoo;
                property Other: IFoo read FOther implements IFoo;
              end;
            procedure TOwn.B; begin end;
            begin
            end.
            """);
        Assert.Empty(Format(result.Diagnostics));
        Assert.Equal(["TBase.$dlg.P.IFoo.A", "TBase.$dlg.P.IFoo.B"], Table(Class(result, "TBase"), "IFoo"));

        // a descendant that re-lists the interface reuses the inherited delegating methods …
        Assert.Equal(["TBase.$dlg.P.IFoo.A", "TBase.$dlg.P.IFoo.B"], Table(Class(result, "TSame"), "IFoo"));
        Assert.DoesNotContain(Class(result, "TSame").Methods, m => m.Name.StartsWith("$dlg.", StringComparison.Ordinal));

        // … unless it supplies its own method or its own delegation
        Assert.Equal(["TBase.$dlg.P.IFoo.A", "TOwn.B"], Table(Class(result, "TOwn"), "IFoo"));
        Assert.Equal(["TAgain.$dlg.P.IFoo.A", "TAgain.$dlg.P.IFoo.B"], Table(Class(result, "TAgain"), "IFoo"));
    }

    [Fact]
    [Trait("Req", "LANG-692")]
    public void Checks_E2662_E2663_E2664_E2651_E2652()
    {
        Assert.Equal(
            [
                "E2651(20,28): Missing implementation of interface method 'IFoo.B'",
                "E2662(23,54): Interface 'IBar' is not listed in the declaration of class 'TOuter'",
                "E2652(24,48): Interface type required",
                "E2664(25,49): Interface 'IFoo' is already implemented by property 'Impl'",
                "E2663(26,51): Property 'NoRead' cannot implement interface 'IFoo': it has no read specifier",
                "E2663(27,63): Property 'Items' cannot implement interface 'IFoo': it is an array property",
                "E2663(28,58): Property 'Shared' cannot implement interface 'IFoo': it is a class property",
                "E2663(29,49): Property 'Number' cannot implement interface 'IFoo': its type is neither a class nor an interface",
                "E2663(30,47): Property 'Other' cannot implement interface 'IFoo': its type does not descend from the interface",
            ],
            BindDiagnostics("""
                program P;
                type
                  IFoo = interface
                    procedure A;
                    procedure B;
                  end;
                  IBar = interface
                    procedure C;
                  end;
                  TImpl = class
                    procedure A;
                    procedure B(X: Integer);
                  end;
                  TOuter = class;
                  TNumbers = class
                    FN: Integer;
                    FBar: IBar;
                    class var FShared: TImpl;
                  end;
                  TOuter = class(TNumbers, IFoo)
                    FImpl: TImpl;
                    function GetItem(I: Integer): TImpl;
                    property Impl: TImpl read FImpl implements IFoo, IBar;
                    property Text: TImpl read FImpl implements TImpl;
                    property Again: TImpl read FImpl implements IFoo;
                    property NoRead: TImpl write FImpl implements IFoo;
                    property Items[I: Integer]: TImpl read GetItem implements IFoo;
                    class property Shared: TImpl read FShared implements IFoo;
                    property Number: Integer read FN implements IFoo;
                    property Other: IBar read FBar implements IFoo;
                  end;
                procedure TImpl.A; begin end;
                procedure TImpl.B(X: Integer); begin end;
                function TOuter.GetItem(I: Integer): TImpl; begin Result := nil end;
                begin
                end.
                """));
    }

    [Fact]
    [Trait("Req", "LANG-692")]
    public void NotOnRecordsOrInterfaces()
    {
        Assert.Equal(
            [
                "E2900(5,31): Feature not supported: implements",
            ],
            BindDiagnostics("""
                type
                  IFoo = interface procedure A; end;
                  TRec = record
                    F: IFoo;
                    property Foo: IFoo read F implements IFoo;
                  end;
                begin
                end.
                """));
    }
}
