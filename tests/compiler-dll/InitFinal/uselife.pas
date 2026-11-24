// covers: CMP-1051, LANG-541
program uselife;
// the library is loaded dynamically only: its units are initialised by LoadLibraryW and finalised by FreeLibrary

uses Windows, SysUtils;

type
  TPing = function: Integer; stdcall;

const
  InitMarker = 'life-init.txt';
  FinalMarker = 'life-final.txt';

var
  H: HMODULE;
  Ping: TPing;
begin
  SetEnvironmentVariableW('LIFELIB_INIT_MARKER', InitMarker);
  SetEnvironmentVariableW('LIFELIB_FINAL_MARKER', FinalMarker);
  Writeln('before load: init ', FileExists(InitMarker), ', final ', FileExists(FinalMarker));
  H := LoadLibraryW('lifelib.dll');
  Writeln('loaded: ', H <> 0);
  Writeln('after load: init ', FileExists(InitMarker), ', final ', FileExists(FinalMarker));
  Ping := TPing(GetProcAddress(H, 'Ping'));
  Writeln('Ping = ', Ping());
  Writeln('host IsLibrary: ', IsLibrary);
  Writeln('freed: ', FreeLibrary(H));
  Writeln('after free: init ', FileExists(InitMarker), ', final ', FileExists(FinalMarker));
end.
