// covers: LANG-462, LANG-463
program ExternalKernel32;
const
  Kernel = 'kernel32.dll';

function ProcessId: Cardinal; stdcall; external Kernel name 'GetCurrentProcessId';
function GetCurrentProcessId: Cardinal; winapi; external 'KERNEL32';

var
  A, B: Cardinal;
begin
  A := ProcessId;
  B := GetCurrentProcessId;
  Writeln(A > 0);
  Writeln(A = B);
end.
