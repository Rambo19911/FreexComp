// covers: LANG-610, LANG-612
program ErrClassVisibility;

uses VisUnit;

type
  TDerived = class(TBase)
    procedure Touch(Other: TBase);
  end;

procedure TDerived.Touch(Other: TBase);
begin
  FShared := 1;
  FHidden := 2;
  Other.FShared := 3;
  FSecret := 4;
end;

var
  B: TBase;
begin
  B := TBase.Create;
  B.FSecret := 5;
  B.FShared := 6;
  B.Free;
end.
