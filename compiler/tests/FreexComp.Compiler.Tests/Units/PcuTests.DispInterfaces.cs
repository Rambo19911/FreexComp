using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Tests.Units;

/// <summary>com-interfaces.md CMP-1206, CMP-1211 (C-303): dispatch interfaces, <c>dispid</c> and <c>safecall</c> in the <c>.pcu</c>.</summary>
public sealed partial class PcuTests
{
    internal const string DispatchUnit = """
        unit U;
        interface
        type
          IInventoryDisp = dispinterface ['{6D0F1C52-3A7E-4B19-9C55-1E2A7B8C9D01}']
            procedure Add(const Key: string; Value: Integer); dispid 1;
            function Exists(const Key: string): Boolean; dispid 2;
            property Count: Integer readonly dispid 3;
            property Sink: Integer writeonly dispid 4;
            property Item[const Key: string]: Integer dispid 0; default;
            property Owner: IInterface dispid 5;
          end;
          ISafe = interface
            ['{6D0F1C52-3A7E-4B19-9C55-1E2A7B8C9D02}']
            procedure Ping; safecall; dispid 20;
            function GetName(N: Integer): string; safecall;
            property Name[N: Integer]: string read GetName dispid 21;
          end;
          TSafeProc = procedure(N: Integer); safecall;
        procedure Later(N: Integer); safecall;
        implementation
        procedure Later(N: Integer);
        begin
        end;
        end.
        """;

    private const string DispatchProgram = """
        program P;
        uses U;
        var
          D: IInventoryDisp;
          S: ISafe;
          Proc: TSafeProc;
          N: Integer;
          Unknown: IInterface;
        begin
          Proc := Later;
          Unknown := D;
          if D <> nil then
          begin
            D.Add('a', D.Count);
            if D.Exists('a') then
              D['a'] := D.Item['a'] + 1;
            D.Sink := 1;
            D.Owner := Unknown;
          end;
          if S <> nil then
            N := Length(S.Name[1]);
        end.
        """;

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1206")]
    [Trait("Req", "CMP-1211")]
    public void DispInterfaceAndSafecallRoundTrip()
    {
        var pcus = Pcus(Files(u: DispatchUnit, program: DispatchProgram));
        var fs = new InMemoryFileSystem()
            .Add(@"C:\p\P.pas", DispatchProgram)
            .AddSystem()
            .Add(@"C:\lib\U.pcu", pcus["U"]).Add(@"C:\lib\U.obj", "");
        var imported = Compilation.Create(Options() with { SearchPaths = [@"C:\lib"] }, [], fs);

        // the flag, the dispids, the access words and the safecall convention are in the file …
        Assert.Contains("\"dispatch\":true", pcus["U"], StringComparison.Ordinal);
        Assert.Matches("\"name\":\"Add\",.*?\"slot\":-1,\"dispid\":1", pcus["U"]);
        Assert.Matches("\"name\":\"Count\",\"type\":\\d+,\"read\":null,\"write\":null,\"index\":null,\"params\":\\[\\],\"isDefault\":false,\"dispid\":3,\"access\":\"readonly\"", pcus["U"]);
        Assert.Contains("\"dispid\":4,\"access\":\"writeonly\"", pcus["U"], StringComparison.Ordinal);
        Assert.Contains("\"isDefault\":true,\"dispid\":0", pcus["U"], StringComparison.Ordinal);
        Assert.Contains("\"dispid\":20", pcus["U"], StringComparison.Ordinal);
        Assert.Contains("\"dispid\":21", pcus["U"], StringComparison.Ordinal);
        Assert.Contains("cc:SafeCall", pcus["U"], StringComparison.Ordinal);

        // … survive write → import → write byte for byte …
        Assert.Equal(pcus["U"], imported.PcuTexts().ToDictionary(t => t.Unit, t => t.Text)["U"]);

        // … and the imported symbols are those of the source: the program binds against the .pcu-only unit
        Assert.Empty(imported.GetDiagnostics(ct: TestContext.Current.CancellationToken).Select(d => d.ToText()));
        var disp = imported.GetUnitSymbol("U")!.InterfaceMembers.OfType<InterfaceTypeSymbol>().Single(i => i.Name == "IInventoryDisp");
        Assert.True(disp.IsDispatchInterface);
        Assert.Equal([1, 2], disp.Methods.Select(m => m.DispId));
        Assert.Equal([1, 1], disp.Methods.Select(m => m.DispatchFlags));
        Assert.Equal(["Count:3:True:False", "Sink:4:False:True", "Item:0:False:False", "Owner:5:False:False"],
            disp.Properties.Select(p => $"{p.Name}:{p.DispId}:{p.IsReadOnly}:{p.IsWriteOnly}"));
        Assert.Equal([2, 0, 2, 2], disp.Properties.Select(p => p.ReadMethod?.DispatchFlags ?? 0));
        Assert.Equal([0, 4, 4, 8], disp.Properties.Select(p => p.WriteMethod?.DispatchFlags ?? 0));
        var safe = imported.GetUnitSymbol("U")!.InterfaceMembers.OfType<InterfaceTypeSymbol>().Single(i => i.Name == "ISafe");
        Assert.False(safe.IsDispatchInterface);
        Assert.Equal([20, null], safe.Methods.Select(m => m.DispId));
        Assert.Equal(21, safe.Properties[0].DispId);
        Assert.All(safe.Methods, m => Assert.Equal("SafeCall", m.CallingConvention.ToString()));
    }
}
