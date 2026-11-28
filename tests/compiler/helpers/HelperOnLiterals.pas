// covers: LANG-997, LANG-998
program HelperOnLiterals;

type
  TIntHelper = record helper for Integer
  const
    Ten = 10;
  public
    function Squared: Integer;
    function IsEven: Boolean;
    function ToText: string;
    class function Parse(const S: string): Integer; static;
  end;

  TInt64Helper = record helper for Int64
    function ToText: string;
  end;

  TCardinalHelper = record helper for Cardinal
    function ToText: string;
  end;

  TDoubleHelper = record helper for Double
    function Half: Double;
  end;

  TStringHelper = record helper for string
    function Len: Integer;
    function Twice: string;
    class function Repeated(const S: string; N: Integer): string; static;
  end;

  TCharHelper = record helper for Char
    function IsDigit: Boolean;
  end;

  TBooleanHelper = record helper for Boolean
    function ToText: string;
  end;

const
  Seven = 7;
  K = Integer.Ten * 2;

function TIntHelper.Squared: Integer;
begin
  Result := Self * Self;
end;

function TIntHelper.IsEven: Boolean;
begin
  Result := not Odd(Self);
end;

function TIntHelper.ToText: string;
begin
  Str(Self, Result);
end;

class function TIntHelper.Parse(const S: string): Integer;
var
  Code: Integer;
begin
  Val(S, Result, Code);
end;

function TInt64Helper.ToText: string;
begin
  Result := 'I64';
end;

function TCardinalHelper.ToText: string;
begin
  Result := 'U32';
end;

function TDoubleHelper.Half: Double;
begin
  Result := Self / 2;
end;

function TStringHelper.Len: Integer;
begin
  Result := Length(Self);
end;

function TStringHelper.Twice: string;
begin
  Result := Self + Self;
end;

class function TStringHelper.Repeated(const S: string; N: Integer): string;
var
  I: Integer;
begin
  Result := '';
  for I := 1 to N do
    Result := Result + S;
end;

function TCharHelper.IsDigit: Boolean;
begin
  Result := (Self >= '0') and (Self <= '9');
end;

function TBooleanHelper.ToText: string;
begin
  if Self then
    Result := 'yes'
  else
    Result := 'no';
end;

var
  A: array[1..2] of Integer;
  I: Integer;

begin
  WriteLn(42.Squared);
  WriteLn((1 + 2).Squared);
  WriteLn(-5.Squared);
  WriteLn((-5).Squared);
  WriteLn($10.Squared);
  WriteLn(3000000000.ToText);
  WriteLn($FFFFFFFF.ToText);
  WriteLn(1.5.Half:0:2);
  WriteLn(1.5e1.Half:0:2);
  WriteLn('abc'.Len);
  WriteLn('abc'.Twice.Len);
  WriteLn(''.Len);
  WriteLn('7'.IsDigit, ' ', 'x'.IsDigit, ' ', #55.IsDigit);
  WriteLn('x'.Len);
  WriteLn(True.ToText, ' ', (1 > 2).ToText);
  WriteLn(Seven.Squared);
  WriteLn(Integer.Ten.Squared);
  WriteLn(K);
  WriteLn(string.Repeated('ab', 2));
  WriteLn(Integer.Parse('12') + 1);
  WriteLn(6.IsEven, ' ', 7.IsEven);
  case Seven - 4 of
    1..5: WriteLn('in 1..5');
  else
    WriteLn('outside');
  end;
  for I := 1 to 2 do
    A[I] := I * 10;
  WriteLn(A[1] + A[2]);
end.
