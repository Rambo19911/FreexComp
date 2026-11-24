unit LifeUnit;
// the marker files are named by the host through environment variables

interface

implementation

uses SysUtils;

procedure Mark(const Variable: string);
var
  F: Text;
begin
  Assign(F, GetEnvironmentVariable(Variable));
  Rewrite(F);
  Close(F);
end;

initialization
  Mark('LIFELIB_INIT_MARKER');
finalization
  Mark('LIFELIB_FINAL_MARKER');
end.
