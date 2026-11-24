// covers: LANG-683, LANG-684
program IntfRecordResult;

{ Record results of interface methods follow the member-function rule: the method-table entry takes a hidden result
  pointer after the reference and returns it, for 4-, 8- and 16-byte records alike. The entries are called through the
  interface and through the raw method table. }

type
  TSize4 = record
    W, H: SmallInt;
  end;

  TSize8 = record
    W, H: Integer;
  end;

  TRect16 = record
    L, T, R, B: Integer;
  end;

  TTriple = array[0..2] of Byte;

  TName = string[15];

  IShape = interface
    ['{5E0C1A11-0B6F-4C1D-9A52-3D1F6E7A8B10}']
    function Small: TSize4;
    function Size: TSize8;
    function Bounds: TRect16;
    function Scaled(Factor: Integer): TSize8;
    function Triple: TTriple;
    function Name: TName;
    function Count: Integer;
  end;

  TShape = class(TInterfacedObject, IShape)
    function Small: TSize4;
    function Size: TSize8; virtual;
    function Bounds: TRect16;
    function Scaled(Factor: Integer): TSize8;
    function Triple: TTriple;
    function Name: TName;
    function Count: Integer;
  end;

  TBigShape = class(TShape)
    function Size: TSize8; override;
  end;

  TRawEntry = function(Self, Res: Pointer): Pointer;
  TRawScaled = function(Self, Res: Pointer; Factor: Integer): Pointer;
  PPointer = ^Pointer;

function TShape.Small: TSize4;
begin
  Result.W := 3;
  Result.H := 4;
end;

function TShape.Size: TSize8;
begin
  Result.W := 30;
  Result.H := 40;
end;

function TShape.Bounds: TRect16;
begin
  Result.L := 1;
  Result.T := 2;
  Result.R := 31;
  Result.B := 42;
end;

function TShape.Scaled(Factor: Integer): TSize8;
begin
  Result.W := 30 * Factor;
  Result.H := 40 * Factor;
end;

function TShape.Triple: TTriple;
begin
  Result[0] := 7;
  Result[1] := 8;
  Result[2] := 9;
end;

function TShape.Name: TName;
begin
  Result := 'shape';
end;

function TShape.Count: Integer;
begin
  Result := 5;
end;

function TBigShape.Size: TSize8;
begin
  Result.W := 300;
  Result.H := 400;
end;

function Slot(const S: IShape; Index: Integer): Pointer;
var
  Table: PPointer;
begin
  Table := PPointer(PPointer(Pointer(S))^);
  Inc(Table, Index);
  Result := Table^;
end;

procedure Show(const S: IShape);
var
  A: TSize4;
  B: TSize8;
  C: TRect16;
  T: TTriple;
  P: Pointer;
begin
  A := S.Small;
  WriteLn('small ', A.W, ' ', A.H);
  B := S.Size;
  WriteLn('size ', B.W, ' ', B.H);
  C := S.Bounds;
  WriteLn('bounds ', C.L, ' ', C.T, ' ', C.R, ' ', C.B);
  B := S.Scaled(2);
  WriteLn('scaled ', B.W, ' ', B.H);
  WriteLn('field of result ', S.Size.H, ' ', S.Bounds.B);
  T := S.Triple;
  WriteLn('triple ', T[0], ' ', T[1], ' ', T[2]);
  WriteLn('name ', S.Name);
  WriteLn('count ', S.Count);
  S.Size;

  { the raw method table: slots 0..2 are IInterface's }
  A.W := 0;
  A.H := 0;
  P := TRawEntry(Slot(S, 3))(Pointer(S), @A);
  WriteLn('raw small ', A.W, ' ', A.H, ' ', P = Pointer(@A));
  B.W := 0;
  B.H := 0;
  P := TRawEntry(Slot(S, 4))(Pointer(S), @B);
  WriteLn('raw size ', B.W, ' ', B.H, ' ', P = Pointer(@B));
  C.L := 0;
  P := TRawEntry(Slot(S, 5))(Pointer(S), @C);
  WriteLn('raw bounds ', C.L, ' ', C.T, ' ', C.R, ' ', C.B, ' ', P = Pointer(@C));
  P := TRawScaled(Slot(S, 6))(Pointer(S), @B, 3);
  WriteLn('raw scaled ', B.W, ' ', B.H, ' ', P = Pointer(@B));
end;

var
  S: IShape;
begin
  S := TShape.Create;
  Show(S);
  S := TBigShape.Create;
  Show(S);
end.
