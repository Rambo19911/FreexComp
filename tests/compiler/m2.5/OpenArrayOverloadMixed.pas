// covers: LANG-430, LANG-442
program OpenArrayOverloadMixed;

procedure P(X: Integer); overload;
begin
  Writeln('int ', X);
end;

procedure P(const A: array of string); overload;
var
  S: string;
begin
  for S in A do
    Write(S, ';');
  Writeln;
end;

begin
  P(['r', 's']);
  P(7);
  P([]);
end.