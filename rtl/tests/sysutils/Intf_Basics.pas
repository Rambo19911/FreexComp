// covers: RTL-154, ABI-095, ABI-096
program Intf_Basics;
{ The SysUtils face of interface support (R-079; the System runtime is Sys_Interfaces): GUIDToString, StringToGUID
  (upper- or lower-case hex, EConvertError for bad text), IsEqualGUID, and Supports for an interface, an object and a
  class without the out parameter (nil instance -> False; no reference left behind), and with the untyped out parameter
  (an interface variable receives a counted reference; nil on failure). }
uses
  SysUtils, TestKit;

type
  IShape = interface
    ['{6F1D2A10-3B4C-4D5E-8F90-A1B2C3D4E5F6}']
    function Area: Integer;
  end;

  IOther = interface
    ['{00112233-4455-6677-8899-AABBCCDDEEFF}']
    procedure Nothing;
  end;

  TSquare = class(TInterfacedObject, IShape)
    function Area: Integer;
  end;

  TPlain = class
  end;

function TSquare.Area: Integer;
begin
  Result := 4;
end;

procedure TestText;
var
  G, H: TGUID;
begin
  Section('GUID text');
  G := StringToGUID('{6F1D2A10-3B4C-4D5E-8F90-A1B2C3D4E5F6}');
  CheckEqI($6F1D2A10, G.D1, 'D1');
  CheckEqI($3B4C, G.D2, 'D2');
  CheckEqI($4D5E, G.D3, 'D3');
  CheckEqI($8F, G.D4[0], 'D4[0]');
  CheckEqI($90, G.D4[1], 'D4[1]');
  CheckEqI($A1, G.D4[2], 'D4[2]');
  CheckEqI($F6, G.D4[7], 'D4[7]');
  CheckEqS('{6F1D2A10-3B4C-4D5E-8F90-A1B2C3D4E5F6}', GUIDToString(G), 'round trip');
  H := StringToGUID('{6f1d2a10-3b4c-4d5e-8f90-a1b2c3d4e5f6}');
  CheckEqB(True, IsEqualGUID(G, H), 'lower-case hex accepted');
  CheckEqS('{6F1D2A10-3B4C-4D5E-8F90-A1B2C3D4E5F6}', GUIDToString(H), 'output is upper-case');
  H.D4[7] := 0;
  CheckEqB(False, IsEqualGUID(G, H), 'IsEqualGUID differs in the last byte');
  H := StringToGUID('{00000000-0000-0000-0000-000000000000}');
  CheckEqS('{00000000-0000-0000-0000-000000000000}', GUIDToString(H), 'zero GUID');
  H := StringToGUID('{00000000-0000-0000-C000-000000000046}');
  CheckEqI($C0, H.D4[0], 'IInterface D4[0]');
  CheckEqI($46, H.D4[7], 'IInterface D4[7]');
end;

procedure BadText(const S: string);
var
  Msg: string;
begin
  Msg := 'no exception';
  try
    StringToGUID(S);
  except
    on E: EConvertError do
      Msg := E.Message;
  end;
  CheckEqS('''' + S + ''' is not a valid GUID value', Msg, 'bad text ' + QuoteStr(S));
end;

procedure TestBadText;
begin
  Section('bad GUID text');
  BadText('');
  BadText('6F1D2A10-3B4C-4D5E-8F90-A1B2C3D4E5F6');
  BadText('{6F1D2A10-3B4C-4D5E-8F90-A1B2C3D4E5F}');
  BadText('{6F1D2A10-3B4C-4D5E-8F90-A1B2C3D4E5F60}');
  BadText('{6F1D2A10-3B4C-4D5E-8F90-A1B2C3D4E5FG}');
  BadText('{6F1D2A10+3B4C-4D5E-8F90-A1B2C3D4E5F6}');
  BadText('{6F1D2A10-3B4C-4D5E-8F90-A1B2C3D4E5F6)');
end;

procedure TestSupports;
var
  Sq: TSquare;
  S: IShape;
  U: IInterface;
  P: TPlain;
  O: TObject;
  N: IInterface;
  IShapeId, IOtherId, IUnknownId: TGUID;
begin
  Section('Supports');
  IShapeId := StringToGUID('{6F1D2A10-3B4C-4D5E-8F90-A1B2C3D4E5F6}');
  IOtherId := StringToGUID('{00112233-4455-6677-8899-AABBCCDDEEFF}');
  IUnknownId := StringToGUID('{00000000-0000-0000-C000-000000000046}');
  Sq := TSquare.Create;
  S := Sq;
  U := Sq;
  CheckEqI(2, Sq.RefCount, 'two references');
  CheckEqB(True, Supports(U, IShapeId), 'interface supports IShape');
  CheckEqB(False, Supports(U, IOtherId), 'interface does not support IOther');
  CheckEqI(2, Sq.RefCount, 'Supports(interface) leaves no reference');
  O := Sq;
  CheckEqB(True, Supports(O, IShapeId), 'object supports IShape');
  CheckEqB(True, Supports(O, IUnknownId), 'object supports IInterface');
  CheckEqB(False, Supports(O, IOtherId), 'object does not support IOther');
  CheckEqI(2, Sq.RefCount, 'Supports(object) leaves no reference');
  CheckEqB(True, Supports(TSquare, IShapeId), 'class supports IShape');
  CheckEqB(False, Supports(TPlain, IShapeId), 'class without interfaces');
  P := TPlain.Create;
  O := P;
  CheckEqB(False, Supports(O, IUnknownId), 'plain object');
  P.Free;
  O := nil;
  CheckEqB(False, Supports(O, IShapeId), 'nil object');
  N := nil;
  CheckEqB(False, Supports(N, IShapeId), 'nil interface');
  S := nil;
  U := nil;
end;

procedure TestSupportsOut;
var
  Sq: TSquare;
  S: IShape;
  U: IInterface;
  X: IOther;
  O: TObject;
  N: IInterface;
  P: Pointer;
  IShapeId, IOtherId: TGUID;
begin
  Section('Supports with out');
  IShapeId := StringToGUID('{6F1D2A10-3B4C-4D5E-8F90-A1B2C3D4E5F6}');
  IOtherId := StringToGUID('{00112233-4455-6677-8899-AABBCCDDEEFF}');
  Sq := TSquare.Create;
  U := Sq;
  CheckEqB(True, Supports(U, IShapeId, S), 'interface supports IShape');
  CheckEqI(4, S.Area, 'the out reference is usable');
  CheckEqI(2, Sq.RefCount, 'the out reference is counted');
  S := nil;
  O := Sq;
  CheckEqB(True, Supports(O, IShapeId, S), 'object supports IShape');
  CheckEqI(4, S.Area, 'object: the out reference is usable');
  CheckEqI(2, Sq.RefCount, 'object: the out reference is counted');
  S := nil;
  CheckEqB(False, Supports(U, IOtherId, X), 'interface does not support IOther');
  Check(X = nil, 'interface: out is nil on failure');
  CheckEqB(False, Supports(O, IOtherId, X), 'object does not support IOther');
  Check(X = nil, 'object: out is nil on failure');
  CheckEqI(1, Sq.RefCount, 'no reference left by failures');
  P := Pointer(1);
  N := nil;
  CheckEqB(False, Supports(N, IShapeId, P), 'nil interface');
  Check(P = nil, 'nil interface: out is nil');
  P := Pointer(1);
  O := nil;
  CheckEqB(False, Supports(O, IShapeId, P), 'nil object');
  Check(P = nil, 'nil object: out is nil');
  U := nil;
end;

begin
  TestText;
  TestBadText;
  TestSupports;
  TestSupportsOut;
  Done;
end.
