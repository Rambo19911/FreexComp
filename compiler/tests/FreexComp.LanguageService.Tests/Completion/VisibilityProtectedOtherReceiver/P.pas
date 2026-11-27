{ context: visibility }
program P;
uses Shapes;
type
  TSquare = class(TShape)
    procedure Copy(Other: TShape);
  end;
procedure TSquare.Copy(Other: TShape);
begin
  Other.{|}
end;
begin
end.
{#has Draw, Name, Free}
{#not Guarded, FName, Hidden}
