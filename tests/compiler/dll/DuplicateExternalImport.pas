// covers: LANG-462, LANG-463
program DuplicateExternalImport;
// several external declarations may bind the same import name: identical ones, and ones that give the
// entry point another signature (here an extra argument the routine ignores, and an integer / pointer
// view of the same parameter); the import is declared once and every call keeps its own signature

const
  Kernel = 'kernel32.dll';

function ProcessId: Cardinal; stdcall; external Kernel name 'GetCurrentProcessId';
function ProcessIdAgain: Cardinal; stdcall; external Kernel name 'GetCurrentProcessId';
function ProcessIdWith(Ignored: NativeInt): Cardinal; stdcall; external Kernel name 'GetCurrentProcessId';

function ModuleByName(Name: PWideChar): NativeUInt; stdcall; external Kernel name 'GetModuleHandleW';
function ModuleByNumber(Name: NativeUInt): NativeUInt; stdcall; external Kernel name 'GetModuleHandleW';

var
  Id: Cardinal;
begin
  Id := ProcessId;
  Writeln('identical declarations agree: ', ProcessIdAgain = Id);
  Writeln('extra argument is ignored: ', ProcessIdWith(42) = Id);
  Writeln('nil as pointer and as number: ', ModuleByName(nil) = ModuleByNumber(0));
  Writeln('by name: ', ModuleByName('kernel32.dll') <> 0);
end.
