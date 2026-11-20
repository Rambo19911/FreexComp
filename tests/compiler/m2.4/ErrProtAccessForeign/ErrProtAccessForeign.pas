// covers: LANG-610, LANG-612, C-260
program ErrProtAccessForeign;

uses ProtBase2, ProtAcc2;

var
  B: TBase;
begin
  B := TBase.Create;
  WriteLn(B.Value);
  WriteLn(TAccess(B).Value);
  TAccess(B).FValue := 1;
  TAccess(B).Bump;
  TAccess.Reset;
  TAccess.FCount := 2;
  B.Free;
end.
