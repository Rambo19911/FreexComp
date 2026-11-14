// covers: LANG-750, LANG-751
program RecordHelperBuiltin;

type
  TIntHelper = record helper for Integer
    const Ten = 10;
    function Squared: Integer;
    function IsEven: Boolean;
    procedure Bump;
    class function Parse(const S: string): Integer; static;
  end;

  TStrHelper = record helper for string
    function Len: Integer;
    function Twice: string;
    procedure Append(const Suffix: string);
  end;

function TIntHelper.Squared: Integer;
begin
  Result := Self * Self;
end;

function TIntHelper.IsEven: Boolean;
begin
  Result := not Odd(Self);
end;

procedure TIntHelper.Bump;
begin
  Inc(Self);
end;

class function TIntHelper.Parse(const S: string): Integer;
var
  I: Integer;
begin
  Result := 0;
  for I := 1 to Length(S) do
    Result := Result * Ten + Ord(S[I]) - Ord('0');
end;

function TStrHelper.Len: Integer;
begin
  Result := Length(Self);
end;

function TStrHelper.Twice: string;
begin
  Result := Self + Self;
end;

procedure TStrHelper.Append(const Suffix: string);
begin
  Self := Self + Suffix;
end;

var
  I: Integer;
  S: string;
begin
  I := 7;
  Writeln(I.Squared, ' ', I.IsEven);
  I.Bump;
  Writeln(I, ' ', I.IsEven);
  Writeln(Integer.Parse('123') + 1);
  Writeln((I + 2).Squared);
  S := 'ab';
  Writeln(S.Len, ' ', S.Twice);
  S.Append('cd');
  Writeln(S, ' ', S.Len);
  Writeln(S.Twice.Len);
end.
