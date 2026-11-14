// covers: LANG-390
program HintUnusedVar;

procedure P;
var
  Unused: Integer;
  Used: Integer;
  A, B: Char;
begin
  Used := 1;
  A := 'a';
  Writeln(Used, A);
end;

function F(Param: Integer): Integer;
var
  Never: Boolean;
begin
  Result := 7;
end;

var
  GlobalUnused: Integer;
begin
  P;
  Writeln(F(0));
end.
