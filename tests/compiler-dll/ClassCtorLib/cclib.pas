// covers: LANG-792, CMP-1396, CMP-1050
library cclib;
// the units' class constructors run at the first process attach, the library file's own types after them; the class
// destructors at process detach, the library's own through its $final entry (it has no managed global)

uses UCCount, UCLibType, SysUtils;

type
  TOwn = record
    class destructor Destroy;
  end;

class destructor TOwn.Destroy;
var
  F: Text;
begin
  Assign(F, GetEnvironmentVariable('CCLIB_OWN_MARKER'));
  Rewrite(F);
  Close(F);
end;

function LibRuns: Integer; stdcall;
begin
  Result := TCounter<Integer>.Runs;
end;

exports
  LibRuns;

end.
