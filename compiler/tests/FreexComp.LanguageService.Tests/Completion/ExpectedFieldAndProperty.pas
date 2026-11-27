{ context: expected-type }
program ExpectedFieldAndProperty;
type
  TColor = (Red, Green);
  TPen = class
  private
    FColor: TColor;
  public
    Width: Integer;
    Tint: TColor;
    property Color: TColor read FColor write FColor;
    function Current: TColor;
    procedure Draw;
  end;
function TPen.Current: TColor;
begin
  Result := FColor;
end;
procedure TPen.Draw;
begin
  Color := {|}
end;
begin
end.
{#ranked Red, Green, Tint, Color, Current, FColor}
{#unranked Width, Draw, Self}
