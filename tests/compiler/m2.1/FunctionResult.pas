// covers: LANG-410, LANG-308, LANG-413
program FunctionResult;

function ViaResult(N: Integer): Integer;
begin
  Result := N + 1;
  Result := Result * 2;
end;

function ViaName(N: Integer): Integer;
begin
  ViaName := N - 1;
end;

function Mixed(N: Integer): Integer;
begin
  Mixed := N;
  Result := Result + 100;
  Mixed := Result + 1;
end;

function IsEven(N: Integer): Boolean;
begin
  IsEven := not Odd(N);
end;

function Letter(I: Integer): Char;
begin
  Result := Chr(Ord('a') + I);
end;

function Big: Int64;
begin
  Result := 1;
  Result := Result shl 40;
end;

procedure Bump(var V: Integer);
begin
  V := V + 5;
end;

function ResultAsVar: Integer;
begin
  Result := 1;
  Bump(Result);
end;

function Accumulate(N: Integer): Integer;
var
  I: Integer;
begin
  Result := 0;
  for I := 1 to N do
    Result := Result + I;
end;

begin
  Writeln(ViaResult(4), ' ', ViaName(4), ' ', Mixed(1));
  Writeln(IsEven(3), ' ', IsEven(10), ' ', Letter(3), ' ', Big);
  Writeln(ResultAsVar, ' ', Accumulate(100));
  Writeln(ViaResult(ViaName(Mixed(0))));
end.
