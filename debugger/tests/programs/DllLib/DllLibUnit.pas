unit DllLibUnit;
// the unit of the test library: what a debug session through the host stops in

interface

function Scale(Value, Factor: Integer): Integer; stdcall;
function Guarded(Value: Integer): Integer; stdcall;

implementation

uses SysUtils;

function Scale(Value, Factor: Integer): Integer; stdcall;
var
  Product: Integer;
begin
  Product := Value * Factor;
  Result := Product + 1;
end;

function Guarded(Value: Integer): Integer; stdcall;
begin
  Result := Value;
  try
    raise Exception.Create('inside library');
  except
    on E: Exception do Result := -Value;
  end;
end;

end.
