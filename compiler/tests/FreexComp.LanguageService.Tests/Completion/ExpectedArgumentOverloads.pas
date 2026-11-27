{ context: expected-type }
program ExpectedArgumentOverloads;
type
  TColor = (Red, Green);
  TMode = (Fast, Slow);
  TSize = (Small, Large);
var
  Alpha: Integer;
procedure Show(A: Integer; Color: TColor); overload;
begin
end;
procedure Show(A: Integer; Mode: TMode); overload;
begin
end;
procedure Show(Size: TSize); overload;
begin
end;
begin
  Show(1, {|});
end.
{#ranked Red, Green, Fast, Slow}
{#unranked Small, Large, Alpha}
