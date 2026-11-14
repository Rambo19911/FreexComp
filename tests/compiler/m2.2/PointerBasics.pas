// covers: LANG-265, LANG-266, LANG-267, CMP-340, CMP-341
program PointerBasics;
type
  PInteger = ^Integer;
  TPoint = record
    X, Y: Integer;
  end;
  PPoint = ^TPoint;
  TPacked = packed record
    B: Byte;
    I: Integer;
  end;
var
  G: Integer;
  GP: PInteger;
  Pt: TPoint;
  PP: PPoint;
  Arr: array[1..3] of Integer;
  Pk: TPacked;
const
  CG: PInteger = @G;
  CNil: Pointer = nil;
  Table: array[0..1] of PInteger = (@G, nil);

procedure Bump(var V: Integer);
begin
  V := V + 1;
end;

procedure SetVia(P: PInteger; V: Integer);
begin
  P^ := V;
end;

function Twice(P: PPoint): Integer;
begin
  Result := P^.X * 2 + P.Y;
end;

var
  P, Q: PInteger;
  U: Pointer;
  L: Integer;
begin
  G := 5;
  P := @G;
  Writeln(P^);
  P^ := 7;
  Writeln(G);
  Q := P;
  Writeln(Q = P, ' ', Q <> P, ' ', P = nil, ' ', Assigned(P));
  P := nil;
  Writeln(P = nil, ' ', Assigned(P));
  U := @L;
  P := U;
  P^ := 42;
  Writeln(L);
  U := Q;
  Writeln(U = @G, ' ', Addr(G) = U);
  SetVia(@L, 9);
  Writeln(L);
  Pt.X := 3;
  Pt.Y := 4;
  PP := @Pt;
  PP^.X := PP^.X + 10;
  PP.Y := PP.Y + 20;
  Writeln(Pt.X, ' ', Pt.Y, ' ', Twice(PP));
  Arr[1] := 1;
  Arr[2] := 2;
  Arr[3] := 3;
  P := @Arr[2];
  P^ := 20;
  Writeln(Arr[1], ' ', Arr[2], ' ', Arr[3]);
  P := @Pt.Y;
  Inc(P^);
  Writeln(Pt.Y);
  Writeln(CG^, ' ', CG = @G, ' ', CNil = nil, ' ', Table[0]^, ' ', Table[1] = nil);
  GP := Table[0];
  Bump(GP^);
  Writeln(G);
  // a var parameter bound to a field of a packed record (less aligned than Integer)
  Pk.B := 1;
  Pk.I := 100;
  Bump(Pk.I);
  Writeln(Pk.B, ' ', Pk.I);
end.
