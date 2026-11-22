// covers: LANG-205, LANG-281, LANG-420
program ErrDistinctBoolVarParam;

{ LANG-281: a var/out argument needs the identical type; a distinct Boolean type (LANG-205) is not identical to
  Boolean in either direction, so each call below is E2010 (LANG-420). }

type
  TFlag = type Boolean;

procedure SetFlag(var F: TFlag);
begin
  F := True;
end;

procedure SetPlain(var B: Boolean);
begin
  B := True;
end;

procedure ClearFlag(out F: TFlag);
begin
  F := False;
end;

var
  B: Boolean;
  F: TFlag;
begin
  SetPlain(F);
  SetFlag(B);
  ClearFlag(B);
end.
