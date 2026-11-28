{ The method bodies of TStringHelper, read by UValueHelpers with the I directive. }

function TStringHelper.GetLength: Integer;
begin
  Result := System.Length(Self);
end;

function TStringHelper.GetChars(Index: Integer): Char;
begin
  Result := Self[Index + 1];
end;

class function TStringHelper.Create(C: Char; Count: Integer): string;
var
  I: Integer;
begin
  Result := '';
  for I := 1 to Count do
    Result := Result + C;
end;

function TStringHelper.Trim: string;
begin
  Result := UValueHelpers.Trim(Self);
end;

function TStringHelper.IsEmpty: Boolean;
begin
  Result := Length = 0;
end;

function TStringHelper.Split(Separator: Char): TArray<string>;
var
  I, Start, Count: Integer;
begin
  Count := 1;
  for I := 1 to Length do
    if Self[I] = Separator then
      Inc(Count);
  SetLength(Result, Count);
  Count := 0;
  Start := 1;
  for I := 1 to Length + 1 do
    if (I > Length) or (Self[I] = Separator) then
    begin
      Result[Count] := System.Copy(Self, Start, I - Start);
      Inc(Count);
      Start := I + 1;
    end;
end;

class function TStringHelper.Join(const Separator: string; const Values: array of string): string;
var
  I: Integer;
begin
  Result := '';
  for I := 0 to High(Values) do
  begin
    if I > 0 then
      Result := Result + Separator;
    Result := Result + Values[I];
  end;
end;
