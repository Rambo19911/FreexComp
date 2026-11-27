// covers: LANG-792, CMP-1396
program usecc;
// the host and the library are separate images: each runs the class constructor of TCounter<Integer> once, with its own class var

uses Windows, SysUtils, UCCount;

type
  TLibRuns = function: Integer; stdcall;

const
  CtorMarker = 'cc-ctor.txt';
  DtorMarker = 'cc-dtor.txt';
  OwnMarker = 'cc-own.txt';

var
  H: HMODULE;
  LibRuns: TLibRuns;
begin
  SetEnvironmentVariableW('CCLIB_CCTOR_MARKER', CtorMarker);
  SetEnvironmentVariableW('CCLIB_CDTOR_MARKER', DtorMarker);
  SetEnvironmentVariableW('CCLIB_OWN_MARKER', OwnMarker);
  Writeln('host Runs = ', TCounter<Integer>.Runs);
  Writeln('before load: cctor ', FileExists(CtorMarker), ', cdtor ', FileExists(DtorMarker));
  H := LoadLibraryW('cclib.dll');
  Writeln('loaded: ', H <> 0);
  Writeln('after load: cctor ', FileExists(CtorMarker), ', cdtor ', FileExists(DtorMarker));
  LibRuns := TLibRuns(GetProcAddress(H, 'LibRuns'));
  Writeln('LibRuns = ', LibRuns());
  Writeln('host Runs = ', TCounter<Integer>.Runs);
  Writeln('freed: ', FreeLibrary(H));
  Writeln('after free: cctor ', FileExists(CtorMarker), ', cdtor ', FileExists(DtorMarker), ', own ', FileExists(OwnMarker));
end.
