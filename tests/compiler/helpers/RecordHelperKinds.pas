// covers: LANG-990, LANG-996
program RecordHelperKinds;

type
  TPoint = record
    X, Y: Integer;
  end;
  TColor = (Red, Green, Blue);
  TColors = set of TColor;
  TTriple = array[0..2] of Integer;
  TInts = array of Integer;
  PPoint = ^TPoint;
  TIntFunc = function(X: Integer): Integer;
  TGetInt = function: Integer;
  TObjectClass = class of TObject;
  TName = string[10];
  TPair<K, V> = record
    Key: K;
    Value: V;
  end;

  TPointHelper = record helper for TPoint
    class var Calls: Integer;
    function Sum: Integer;
    procedure Offset(DX, DY: Integer);
    class function Make(X, Y: Integer): TPoint; static;
    property Total: Integer read Sum;
  end;

  TColorHelper = record helper for TColor
    function ToString: string;
    function Next: TColor;
    class function Parse(const S: string): TColor; static;
  end;

  TColorsHelper = record helper for TColors
    function Count: Integer;
  end;

  TTripleHelper = record helper for TTriple
    function Sum: Integer;
    procedure Clear;
  end;

  TIntsHelper = record helper for TInts
    function GetCount: Integer;
    procedure Add(V: Integer);
    property Count: Integer read GetCount;
  end;

  TPPointHelper = record helper for PPoint
    function IsNil: Boolean;
    function SumOrZero: Integer;
  end;

  TIntFuncHelper = record helper for TIntFunc
    function Twice(X: Integer): Integer;
  end;

  // a value of a function type without parameters is the receiver, it is not called first
  TGetIntHelper = record helper for TGetInt
    function Doubled: Integer;
    function IsSet: Boolean;
  end;

  TObjectClassHelper = record helper for TObjectClass
    function IsRoot: Boolean;
    function Make: TObject;
  end;

  TNameHelper = record helper for TName
    function Len: Integer;
  end;

  TPairHelper = record helper for TPair<Integer, string>
    function Describe: string;
  end;

  TLeaf = class
  end;

function TPointHelper.Sum: Integer;
begin
  Inc(Calls);
  Result := X + Y;
end;

procedure TPointHelper.Offset(DX, DY: Integer);
begin
  X := X + DX;
  Self.Y := Self.Y + DY;
end;

class function TPointHelper.Make(X, Y: Integer): TPoint;
begin
  Result.X := X;
  Result.Y := Y;
end;

function TColorHelper.ToString: string;
begin
  case Self of
    Red: Result := 'Red';
    Green: Result := 'Green';
  else
    Result := 'Blue';
  end;
end;

function TColorHelper.Next: TColor;
begin
  if Self = Blue then
    Result := Red
  else
    Result := Succ(Self);
end;

class function TColorHelper.Parse(const S: string): TColor;
begin
  if S = 'Green' then
    Result := Green
  else if S = 'Blue' then
    Result := Blue
  else
    Result := Red;
end;

function TColorsHelper.Count: Integer;
var
  C: TColor;
begin
  Result := 0;
  for C := Red to Blue do
    if C in Self then
      Inc(Result);
end;

function TTripleHelper.Sum: Integer;
var
  I: Integer;
begin
  Result := 0;
  for I := 0 to 2 do
    Result := Result + Self[I];
end;

procedure TTripleHelper.Clear;
var
  I: Integer;
begin
  for I := 0 to 2 do
    Self[I] := 0;
end;

function TIntsHelper.GetCount: Integer;
begin
  Result := Length(Self);
end;

procedure TIntsHelper.Add(V: Integer);
begin
  SetLength(Self, Length(Self) + 1);
  Self[High(Self)] := V;
end;

function TPPointHelper.IsNil: Boolean;
begin
  Result := Self = nil;
end;

function TPPointHelper.SumOrZero: Integer;
begin
  if Self = nil then
    Result := 0
  else
    Result := Self^.X + Self^.Y;
end;

function TIntFuncHelper.Twice(X: Integer): Integer;
begin
  Result := Self(Self(X));
end;

function TGetIntHelper.Doubled: Integer;
begin
  Result := Self() + Self();
end;

function TGetIntHelper.IsSet: Boolean;
begin
  Result := Assigned(Self);
end;

function TObjectClassHelper.IsRoot: Boolean;
begin
  Result := Self = TObject;
end;

function TObjectClassHelper.Make: TObject;
begin
  Result := Self.Create;
end;

function TNameHelper.Len: Integer;
begin
  Result := Length(Self);
end;

function TPairHelper.Describe: string;
var
  S: string;
begin
  Str(Key, S);
  Result := S + '=' + Value;
end;

function AddThree(X: Integer): Integer;
begin
  Result := X + 3;
end;

function Seven: Integer;
begin
  Result := 7;
end;

var
  P: TPoint;
  Q: PPoint;
  C: TColor;
  Colors: TColors;
  T: TTriple;
  A: TInts;
  F: TIntFunc;
  G: TGetInt;
  K: TObjectClass;
  O: TObject;
  N: TName;
  Pair: TPair<Integer, string>;
begin
  P := TPoint.Make(3, 4);
  Writeln(P.Sum, ' ', P.Total);
  P.Offset(10, 20);
  Writeln(P.X, ' ', P.Y, ' ', P.Total);
  Writeln(TPoint.Make(1, 2).Sum);
  Writeln(TPoint.Calls, ' ', P.Calls);

  C := Blue;
  Writeln(C.ToString, ' ', C.Next.ToString, ' ', Green.ToString, ' ', Green.Next.ToString);
  Writeln(TColor.Parse('Green').ToString, ' ', TColor.Parse('?').ToString);

  Colors := [Red, Blue];
  Writeln(Colors.Count);
  Colors := [];
  Writeln(Colors.Count);

  T[0] := 1;
  T[1] := 2;
  T[2] := 3;
  Writeln(T.Sum);
  T.Clear;
  Writeln(T.Sum);

  Writeln(A.Count);
  A.Add(5);
  A.Add(7);
  Writeln(A.Count, ' ', A[0], ' ', A[1]);

  Q := nil;
  Writeln(Q.IsNil, ' ', Q.SumOrZero);
  Q := @P;
  Writeln(Q.IsNil, ' ', Q.SumOrZero);

  F := AddThree;
  Writeln(F.Twice(1));

  Writeln(G.IsSet);
  G := Seven;
  Writeln(G.IsSet, ' ', G.Doubled);

  K := TObject;
  Writeln(K.IsRoot);
  K := TLeaf;
  O := K.Make;
  Writeln(K.IsRoot, ' ', O.ClassName);
  O.Free;

  N := 'helper';
  Writeln(N.Len);

  Pair.Key := 7;
  Pair.Value := 'seven';
  Writeln(Pair.Describe);
end.