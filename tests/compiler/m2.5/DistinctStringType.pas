// covers: LANG-205, LANG-281, LANG-442, CMP-405
program DistinctStringType;

{ LANG-205: `type string` is a new type with the representation of string: overloads on string and the distinct
  type resolve exactly (a converted argument such as a literal prefers string), the RTTI name is the declared one,
  and values assign both ways (LANG-280 rule 1 does not apply, the string rule does). An alias stays the same type. }

uses TypInfo;

type
  TMyStr = type string;
  TAliasStr = string;
  TMyAnsi = type AnsiString;

procedure P(const S: string); overload;
begin
  Writeln('string ', S);
end;

procedure P(const S: TMyStr); overload;
begin
  Writeln('TMyStr ', S);
end;

procedure Append(var S: TMyStr; const Tail: string);
begin
  S := S + Tail;
end;

procedure AppendPlain(var S: string);
begin
  S := S + '!';
end;

function Twice(const S: TMyStr): TMyStr;
begin
  Result := S + S;
end;

var
  M: TMyStr;
  S: string;
  A: TAliasStr;
  N: TMyAnsi;
begin
  M := 'abc';
  S := 'xyz';
  P(M);
  P(S);
  P('literal');
  P('y');
  { assignment both ways }
  S := M;
  M := S + 'd';
  Writeln(S, ' ', M, ' ', Length(M));
  Append(M, 'ef');
  Writeln(M);
  { an alias is still string: a var string parameter accepts it }
  A := 'alias';
  AppendPlain(A);
  P(A);
  Writeln(Twice(M), ' ', M = 'abcdef', ' ', S < M);
  N := 'ansi';
  M := N;
  Writeln(M);
  Writeln(GetTypeName(TypeInfo(TMyStr)));
  Writeln(GetTypeName(TypeInfo(TAliasStr)));
  Writeln(GetTypeName(TypeInfo(TMyAnsi)));
  Writeln(GetTypeName(TypeInfo(string)));
end.
