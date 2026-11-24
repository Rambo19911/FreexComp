// covers: LANG-680, LANG-681
program GuidOfInterface;

{ An interface type identifier where a TGUID is required denotes the interface's GUID: argument of a value or const
  parameter (also of an overloaded routine), assignment, typed constant, initialised variable, function result.
  A typed TGUID constant also takes a GUID string. }

type
  IFoo = interface
    ['{01234567-89AB-CDEF-1021-324354657687}']
  end;

  IBar = interface(IFoo)
    ['{A0B1C2D3-E4F5-0617-2839-4A5B6C7D8E9F}']
  end;

  TBytes16 = array[0..15] of Byte;

const
  FooId: TGUID = IFoo;
  PersistId: TGUID = '{0000010C-0000-0000-C000-000000000046}';
  LowerId: TGUID = '{a0b1c2d3-e4f5-0617-2839-4a5b6c7d8e9f}';
  Ids: array[0..1] of TGUID = (IBar, '{00000000-0000-0000-C000-000000000046}');

var
  BarId: TGUID = IBar;
  TextId: TGUID = '{01234567-89AB-CDEF-1021-324354657687}';

procedure Dump(const Title: string; const G: TGUID);
const
  Hex: string = '0123456789ABCDEF';
var
  B: TBytes16;
  I: Integer;
begin
  Move(G, B, SizeOf(B));
  Write(Title, ':');
  for I := 0 to 15 do
    Write(' ', Hex[B[I] shr 4 + 1], Hex[B[I] and 15 + 1]);
  WriteLn;
end;

procedure DumpValue(G: TGUID);
begin
  Dump('value', G);
end;

function Same(const A, B: TGUID): Boolean;
var
  I: Integer;
begin
  Result := (A.D1 = B.D1) and (A.D2 = B.D2) and (A.D3 = B.D3);
  for I := 0 to 7 do
    Result := Result and (A.D4[I] = B.D4[I]);
end;

procedure Pick(N: Integer); overload;
begin
  WriteLn('integer ', N);
end;

procedure Pick(const G: TGUID); overload;
begin
  Dump('picked', G);
end;

function IdOfBar: TGUID;
begin
  Result := IBar;
end;

var
  G: TGUID;
begin
  Dump('const', FooId);
  Dump('string', PersistId);
  Dump('lower', LowerId);
  Dump('array0', Ids[0]);
  Dump('array1', Ids[1]);
  Dump('var', BarId);
  Dump('text', TextId);
  Dump('direct', IFoo);
  DumpValue(IBar);
  Pick(7);
  Pick(IFoo);
  G := IBar;
  Dump('assigned', G);
  G := (IFoo);
  Dump('again', G);
  Dump('result', IdOfBar);
  WriteLn(Same(IFoo, FooId));
  WriteLn(Same(IFoo, IBar));
  WriteLn(Same(G, IFoo));
  WriteLn(Same(BarId, LowerId));
  WriteLn(Same(TextId, GuidOfInterface.IFoo));
end.
