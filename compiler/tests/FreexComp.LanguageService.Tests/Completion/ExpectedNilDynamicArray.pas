{ context: expected-type }
program ExpectedNilDynamicArray;
type
  TNumbers = array of Integer;
var
  Numbers: TNumbers;
  Alpha: Integer;
begin
  Numbers := {|}
end.
{#ranked nil, Numbers}
{#unranked Alpha}
