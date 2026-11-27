{ context: expected-type }
program ExpectedNoneUnchanged;
type
  TColor = (Red, Green);
var
  Alpha, Total: Integer;
  Paint: TColor;
begin
  Total := Alpha + {|}
end.
{#has Alpha, Total, Red}
{#unranked Alpha, Total, Red, Paint, nil}
