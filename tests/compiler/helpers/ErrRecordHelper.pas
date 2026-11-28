// covers: LANG-996
program ErrRecordHelper;

type
  TPoint = record
    X, Y: Integer;
  end;

  TIntHelper = record helper for Integer
    function Squared: Integer;
    procedure Bump;
    procedure BumpTwice;
  end;

  TStringHelper = record helper for string
    function Len: Integer;
    procedure Append(const Suffix: string);
  end;

  TPointHelper = record helper for TPoint
    Count: Integer;                           // row 10: a helper has no instance fields
    function Sum: Integer;
    procedure Offset(DX, DY: Integer);
    procedure Zero;
    function AddrOf: Pointer;
  end;

function TIntHelper.Squared: Integer;
begin
  Result := Self * Self;
end;

procedure TIntHelper.Bump;
begin
  Inc(Self);
end;

// mutating through the method it calls (rule 4)
procedure TIntHelper.BumpTwice;
begin
  Bump;
  Bump;
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

// the fields are assigned without `Self.`
procedure TPointHelper.Offset(DX, DY: Integer);
begin
  X := X + DX;
  Y := Y + DY;
end;

// Self is passed to an untyped var parameter (rule 2)
procedure TPointHelper.Zero;
begin
  FillChar(Self, SizeOf(Self), 0);
end;

// the address of Self is taken (rule 3)
function TPointHelper.AddrOf: Pointer;
begin
  Result := @Self;
end;

const
  Five = 5;
  Origin: TPoint = (X: 0; Y: 0);

procedure Rows(const P: TPoint; const S: string; const N: Integer; const A: array of Integer);
var
  I: Integer;
begin
  P.Offset(1, 1);                             // row 1
  S.Append('x');                              // row 2
  N.BumpTwice;                                // row 3
  Origin.Zero;                                // row 4
  if Origin.AddrOf = nil then                 // row 5
    Writeln('nil');
  Five.Bump;                                  // row 6
  for I := 1 to 3 do
    I.Bump;                                   // row 7
  A[0].Bump;                                  // row 8
  Writeln(P.Sum, S.Len, N.Squared, Five.Squared);   // row 9: methods that are not mutating
end;

begin
  Rows(Origin, 'abc', 4, [1, 2]);
end.