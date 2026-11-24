// covers: RTL-958, RTL-964
library faillib;

uses FailFirst, FailSecond;

function Ping: Integer; stdcall;
begin
  Result := 1;
end;

exports
  Ping;

begin
  Mark('library-block.txt');
end.
