// covers: LANG-602, LANG-622
program ErrAbstractCreate;

type
  TShape = class abstract
    procedure Draw; virtual;
  end;

  TPartial = class
    procedure Draw; virtual; abstract;
  end;

procedure TShape.Draw;
begin
end;

var
  S: TShape;
  P: TPartial;
begin
  P := TPartial.Create;
  S := TShape.Create;
  P.Free;
  S.Free;
end.
