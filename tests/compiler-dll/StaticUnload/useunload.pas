// covers: CMP-1071
program useunload;
// load-time linking: the library is unloaded when the process ends, after this program has finished

uses SysUtils;

function Twice(A: Integer): Integer; stdcall; external 'unloadlib.dll';

begin
  Writeln('Twice(21) = ', Twice(21));
  Writeln('marker while running: ', FileExists('unloaded.txt'));
end.
