// covers: LANG-205, LANG-281, LANG-420
program ErrDistinctVarParam;

{ LANG-281: a var/out argument needs the identical type; a distinct type (LANG-205) is not identical to its source
  in either direction, so each call below is E2010 (LANG-420). }

type
  TMyStr = type string;
  TMyDbl = type Double;

procedure SetMine(var S: TMyStr);
begin
  S := 'mine';
end;

procedure SetPlain(var S: string);
begin
  S := 'plain';
end;

procedure SetDbl(out D: TMyDbl);
begin
  D := 1;
end;

var
  S: string;
  M: TMyStr;
  D: Double;
begin
  SetMine(S);
  SetPlain(M);
  SetDbl(D);
end.
