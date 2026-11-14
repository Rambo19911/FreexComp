unit TvLib;

interface

threadvar
  Hits: Integer;
  Name: array[0..3] of Char;

procedure Hit;
function HiddenValue: Int64;

implementation

threadvar
  Hidden: Int64;

procedure Hit;
begin
  Hits := Hits + 1;
  Hidden := Hidden + 10;
end;

function HiddenValue: Int64;
begin
  Result := Hidden;
end;

initialization
  Hit;
end.
