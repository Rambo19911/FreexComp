{ context: expected-type }
program ExpectedSubrangeIntegerUnchanged;
type
  TPercent = 0..100;
const
  Half = 50;
var
  Alpha: Integer;
  Small: Byte;
  Done, Share: TPercent;
begin
  Share := {|}
end.
{#ranked Done, Share}
{#unranked Half, Alpha, Small}
