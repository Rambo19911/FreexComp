// covers: CMP-1051
library faillib;

uses FirstUnit, BadUnit;

function Ping: Integer; stdcall;
begin
  Result := 1;
end;

exports
  Ping;

begin
  Mark('library-block.txt');
end.
