{ context: expected-type }
program ExpectedArgumentMethod;
type
  TColor = (Red, Green);
  TPen = class
    procedure SetColor(Value: TColor);
  end;
procedure TPen.SetColor(Value: TColor);
begin
end;
var
  Alpha: Integer;
  Pen: TPen;
begin
  Pen.SetColor({|});
end.
{#ranked Red, Green}
{#unranked Alpha, Pen}
