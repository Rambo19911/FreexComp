unit UCLibType;
// the marker files are named by the host through environment variables

interface

type
  TLibType = class
    class constructor Create;
    class destructor Destroy;
  end;

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

class constructor TLibType.Create;
begin
  Mark('CCLIB_CCTOR_MARKER');
end;

class destructor TLibType.Destroy;
begin
  Mark('CCLIB_CDTOR_MARKER');
end;

end.
