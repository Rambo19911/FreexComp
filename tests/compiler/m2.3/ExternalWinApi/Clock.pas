unit Clock;

interface

const
  Kernel = 'kernel32.dll';

function TickCount: Cardinal; stdcall; external Kernel name 'GetTickCount';
function ProcessId: Cardinal; winapi; external 'KERNEL32' name 'GetCurrentProcessId';

var
  SharedCounter: Integer = 41; public name 'clock_shared_counter';

implementation

function Twice(X: Integer): Integer; public name 'clock_twice';
begin
  Result := X * 2;
end;

end.