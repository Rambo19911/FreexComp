// covers: LANG-620
program ErrMethodNotImplemented;

type
  TShape = class
    procedure Draw;
    function Area: Double;
    procedure Done;
  end;

procedure TShape.Draw;
begin
end;

procedure TShape.Resize;
begin
end;

begin
end.
