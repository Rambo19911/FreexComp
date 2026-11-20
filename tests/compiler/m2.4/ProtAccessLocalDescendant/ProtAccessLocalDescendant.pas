// covers: LANG-610, C-260
program ProtAccessLocalDescendant;

uses ProtBase;

type
  TAccess = class(TBase);
  TAccessRef = class of TAccess;

var
  B: TBase;
  R: TAccessRef;
begin
  B := TBase.Create(21);
  WriteLn(TAccess(B).Value);
  TAccess(B).Value := 5;
  WriteLn(TAccess(B).FValue);
  TAccess(B).FValue := 7;
  WriteLn(TAccess(B).Twice);
  WriteLn(TAccess.Kind);
  TAccess.FCount := TAccess.FCount + 10;
  WriteLn(TAccess.Count);
  R := TAccess;
  WriteLn(R.Kind, ' ', R.Count);
  with TAccess(B) do
    WriteLn(Value + FValue);
  B.Free;
end.
