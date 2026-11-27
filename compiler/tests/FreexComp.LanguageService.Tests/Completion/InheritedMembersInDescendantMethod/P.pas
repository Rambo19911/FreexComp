{ context: inherited-members }
program P;
uses Shapes;
type
  TSquare = class(TShape)
    procedure Resize(Amount: Integer);
  end;
procedure TSquare.Resize(Amount: Integer);
begin
  {|}
end;
begin
end.
{#has Amount, Self, Resize, Draw, Guarded, Name, Free}
{#kind Self=Parameter}
{#not FName, Hidden}
{#top Amount}
