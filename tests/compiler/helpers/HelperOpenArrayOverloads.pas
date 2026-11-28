// covers: LANG-442, LANG-433, LANG-996
program HelperOpenArrayOverloads;

{ A bracket constructor argument against overloads whose open-array parameters have different element types (C-458):
  array of Char / array of string for Split, array of string / array of const for Join; unit-level routines and the
  methods of a record helper for string, called on variables. }

type
  TParts = array of string;

  TTextHelper = record helper for string
    function Split(const Separators: array of Char): TParts; overload;
    function Split(const Separators: array of string): TParts; overload;
    function Join(const Values: array of string): string; overload;
    function Join(const Values: array of const): string; overload;
  end;

function IsAt(const S: string; I: Integer; const Sep: string): Boolean;
begin
  Result := (Sep <> '') and (Copy(S, I, Length(Sep)) = Sep);
end;

function SplitBy(const S: string; const Separators: array of string): TParts;
var
  I, Start, K: Integer;
  Found: Boolean;
begin
  SetLength(Result, 0);
  Start := 1;
  I := 1;
  while I <= Length(S) do
  begin
    Found := False;
    for K := 0 to High(Separators) do
      if IsAt(S, I, Separators[K]) then
      begin
        SetLength(Result, Length(Result) + 1);
        Result[High(Result)] := Copy(S, Start, I - Start);
        I := I + Length(Separators[K]);
        Start := I;
        Found := True;
        Break;
      end;
    if not Found then
      Inc(I);
  end;
  SetLength(Result, Length(Result) + 1);
  Result[High(Result)] := Copy(S, Start, Length(S) - Start + 1);
end;

function SplitChars(const S: string; const Separators: array of Char): TParts;
var
  Texts: array of string;
  K: Integer;
begin
  Write('[chars] ');
  SetLength(Texts, Length(Separators));
  for K := 0 to High(Separators) do
    Texts[K] := Separators[K];
  Result := SplitBy(S, Texts);
end;

function SplitStrings(const S: string; const Separators: array of string): TParts;
begin
  Write('[strings] ');
  Result := SplitBy(S, Separators);
end;

function Split(const S: string; const Separators: array of Char): TParts; overload;
begin
  Result := SplitChars(S, Separators);
end;

function Split(const S: string; const Separators: array of string): TParts; overload;
begin
  Result := SplitStrings(S, Separators);
end;

function JoinStrings(const Sep: string; const Values: array of string): string;
var
  K: Integer;
begin
  Result := '[strings] ';
  for K := 0 to High(Values) do
  begin
    if K > 0 then
      Result := Result + Sep;
    Result := Result + Values[K];
  end;
end;

function JoinConsts(const Sep: string; const Values: array of const): string;
var
  K: Integer;
begin
  Result := '[const] ';
  for K := 0 to High(Values) do
  begin
    if K > 0 then
      Result := Result + Sep;
    case Values[K].VType of
      vtInteger: Result := Result + Chr(Ord('0') + Values[K].VInteger mod 10);
      vtWideChar: Result := Result + Values[K].VWideChar;
      vtUnicodeString: Result := Result + string(PChar(Values[K].VUnicodeString));
    else
      Result := Result + '?';
    end;
  end;
end;

function Join(const Sep: string; const Values: array of string): string; overload;
begin
  Result := JoinStrings(Sep, Values);
end;

function Join(const Sep: string; const Values: array of const): string; overload;
begin
  Result := JoinConsts(Sep, Values);
end;

function TTextHelper.Split(const Separators: array of Char): TParts;
begin
  Result := SplitChars(Self, Separators);
end;

function TTextHelper.Split(const Separators: array of string): TParts;
begin
  Result := SplitStrings(Self, Separators);
end;

function TTextHelper.Join(const Values: array of string): string;
begin
  Result := JoinStrings(Self, Values);
end;

function TTextHelper.Join(const Values: array of const): string;
begin
  Result := JoinConsts(Self, Values);
end;

procedure Show(const Parts: TParts);
var
  K: Integer;
begin
  Write(Length(Parts), ':');
  for K := 0 to High(Parts) do
    Write(' <', Parts[K], '>');
  Writeln;
end;

var
  Text, Sep, A, B: string;
  C: Char;
  N: Integer;
begin
  Text := 'a,b;c, d';
  Sep := ', ';
  A := 'x';
  B := 'y';
  C := ';';
  N := 7;

  Show(Split(Text, [',']));
  Show(Split(Text, [',', ';']));
  Show(Split(Text, [C]));
  Show(Split(Text, [', ']));
  Show(Split(Text, [', ', ';']));
  Show(Split(Text, [Sep]));
  Writeln(Join(Sep, ['a', 'b']));
  Writeln(Join(Sep, [A, B]));
  Writeln(Join(Sep, [N, 'a', C]));
  Writeln(Join(Sep, [A, N]));

  Show(Text.Split([',']));
  Show(Text.Split([',', ';']));
  Show(Text.Split([C]));
  Show(Text.Split([', ']));
  Show(Text.Split([', ', C]));
  Show(Text.Split([Sep]));
  Writeln(Sep.Join(['a', 'b']));
  Writeln(Sep.Join([A, B]));
  Writeln(Sep.Join([N, 'a', C]));
  Writeln(Sep.Join([A, N]));
end.
