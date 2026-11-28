// covers: LANG-996
program HelperReceivers;

type
  TPoint = record
    X, Y: Integer;
  end;
  PPoint = ^TPoint;

  TNamed = record
    Name: string;
    Pos: TPoint;
  end;

  TBox = class
  private
    FPos: TPoint;
    FCount: Integer;
    function GetCount: Integer;
  public
    property Pos: TPoint read FPos write FPos;
    property Count: Integer read GetCount;
  end;

  TIntHelper = record helper for Integer
    function Squared: Integer;
    procedure Bump;
    procedure BumpTwice;
  end;

  TCharHelper = record helper for Char
    function IsUpper: Boolean;
    procedure MakeUpper;
  end;

  TStringHelper = record helper for string
    function Len: Integer;
    procedure Append(const Suffix: string);
  end;

  TPointHelper = record helper for TPoint
    function Sum: Integer;
    procedure Offset(DX, DY: Integer);
  end;

function TBox.GetCount: Integer;
begin
  Result := FCount;
end;

function TIntHelper.Squared: Integer;
begin
  Result := Self * Self;
end;

procedure TIntHelper.Bump;
begin
  Inc(Self);
end;

// a helper method that calls another one on Self
procedure TIntHelper.BumpTwice;
begin
  Bump;
  Self.Bump;
end;

function TCharHelper.IsUpper: Boolean;
begin
  Result := (Self >= 'A') and (Self <= 'Z');
end;

procedure TCharHelper.MakeUpper;
begin
  if (Self >= 'a') and (Self <= 'z') then
    Self := Chr(Ord(Self) - 32);
end;

function TStringHelper.Len: Integer;
begin
  Result := Length(Self);
end;

procedure TStringHelper.Append(const Suffix: string);
begin
  Self := Self + Suffix;
end;

function TPointHelper.Sum: Integer;
begin
  Result := X + Y;
end;

procedure TPointHelper.Offset(DX, DY: Integer);
begin
  X := X + DX;
  Y := Y + DY;
end;

var
  Source: TPoint;

function MakePoint: TPoint;
begin
  Result := Source;
end;

procedure ByVar(var P: TPoint);
begin
  P.Offset(1, 1);
end;

procedure ByOut(out N: Integer);
begin
  N := 6;
  N.Bump;
end;

procedure ByValue(P: TPoint);
begin
  P.Offset(100, 100);
  Writeln('value parameter inside: ', P.X, ' ', P.Y);
end;

procedure ByConst(const S: string; const P: TPoint);
begin
  Writeln('const parameters: ', S.Len, ' ', P.Sum);
end;

var
  V: TPoint;
  Named: TNamed;
  Fixed: array[0..1] of Integer;
  Dyn: array of Integer;
  Ptr: PPoint;
  Box: TBox;
  N: Integer;
  S, T: string;
begin
  V.X := 1;
  V.Y := 2;
  V.Offset(10, 20);
  Writeln('variable: ', V.X, ' ', V.Y);

  Named.Pos.X := 3;
  Named.Pos.Y := 4;
  Named.Pos.Offset(1, 1);
  Writeln('record field: ', Named.Pos.X, ' ', Named.Pos.Y);

  Fixed[1] := 7;
  Fixed[1].Bump;
  Writeln('static array element: ', Fixed[1]);

  SetLength(Dyn, 2);
  Dyn[1] := 8;
  Dyn[1].Bump;
  Writeln('dynamic array element: ', Dyn[1]);

  New(Ptr);
  Ptr^.X := 5;
  Ptr^.Y := 6;
  Ptr^.Offset(1, 2);
  Writeln('dereferenced pointer: ', Ptr^.X, ' ', Ptr^.Y);
  Dispose(Ptr);

  ByVar(V);
  Writeln('var parameter: ', V.X, ' ', V.Y);

  ByOut(N);
  Writeln('out parameter: ', N);

  ByValue(V);
  Writeln('value parameter, the caller: ', V.X, ' ', V.Y);

  Source.X := 20;
  Source.Y := 22;
  MakePoint.Offset(1, 1);
  Writeln('function result: ', Source.X, ' ', Source.Y, ' ', MakePoint.Sum);

  Box := TBox.Create;
  Box.FPos.X := 30;
  Box.FPos.Y := 31;
  Box.FCount := 5;
  Box.Pos.Offset(1, 1);
  Box.Count.Bump;
  Writeln('property read: ', Box.Pos.X, ' ', Box.Pos.Y, ' ', Box.Count, ' ', Box.Count.Squared);
  Box.Free;

  S := 'abc';
  ByConst(S, V);

  Named.Name := 'ab';
  with Named do
    Name.Append('cd');
  Writeln('string field through with: ', Named.Name);

  N := 1;
  N.BumpTwice;
  Writeln('a method that calls another one on Self: ', N);

  T := S;
  S[1].MakeUpper;
  Writeln('character of a string: ', S, ' ', T, ' ', S[1].IsUpper, ' ', T[1].IsUpper);
end.