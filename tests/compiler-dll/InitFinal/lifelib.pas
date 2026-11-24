// covers: LANG-541, CMP-1050, CMP-1051
library lifelib;

uses LifeUnit;

function Ping: Integer; stdcall;
begin
  Result := Ord(IsLibrary);
end;

exports
  Ping;

end.
