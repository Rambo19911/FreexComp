{ context: with }
program WithAfterDotUnchanged;
type
  TEngine = record
    Power: Integer;
  end;
  TWheel = record
    Size: Integer;
  end;
var
  E: TEngine;
  W: TWheel;
begin
  with E do
    W.{|}
end.
{#has Size}
{#not Power, E, W}
