// covers: RTL-950, RTL-951, RTL-952, RTL-955, RTL-963
program uselife;
// dynamic load and unload: what System reports inside the library, and the order of initialization and finalization

uses Windows, SysUtils, LifeTypes;

type
  TDescribe = procedure(Loaded: NativeUInt; Info: PLifeInfo); stdcall;
  TSay = procedure; stdcall;

procedure PrintOrder(const Title: string);
var
  F: Text;
  Line: string;
begin
  Writeln(Title, ':');
  Assign(F, 'life-order.txt');
  Reset(F);
  while not Eof(F) do
  begin
    Readln(F, Line);
    Writeln('  ', Line);
  end;
  Close(F);
end;

var
  LibraryPath: string;
  H: HMODULE;
  Describe: TDescribe;
  Say: TSay;
  Info: TLifeInfo;
begin
  LibraryPath := ExtractFilePath(ParamStr(0)) + 'lifelib.dll';
  Writeln('order file before the load: ', FileExists('life-order.txt'));
  H := LoadLibraryW(PChar(LibraryPath));
  Writeln('loaded: ', H <> 0);
  PrintOrder('after LoadLibraryW');

  Describe := TDescribe(GetProcAddress(H, 'Describe'));
  FillChar(Info, SizeOf(Info), 0);
  Describe(H, @Info);
  Writeln('library IsLibrary: ', Info.IsLibrary <> 0);
  Writeln('host IsLibrary: ', IsLibrary);
  Writeln('library HInstance is the handle of LoadLibraryW: ', Info.HInstanceIsLoaded <> 0);
  Writeln('host HInstance is another module: ', HInstance <> H);
  Writeln('library ParamStr(0) is the host program: ', SameText(StrPas(@Info.ParamStr0[0]), ParamStr(0)));
  Writeln('library ParamCount: ', Info.ParamCount, ', ParamStr(2): ', StrPas(@Info.ParamStr2[0]));
  Writeln('library IsConsole: ', Info.IsConsole <> 0);
  Writeln('library MainThreadID is the loading thread: ', Info.MainThreadID = GetCurrentThreadId);
  Writeln('GetModuleName(HInstance) in the library is the library file: ', SameText(StrPas(@Info.ModuleName[0]), LibraryPath));
  Writeln('GetModuleName(library handle) in the host: ', SameText(GetModuleName(H), LibraryPath));
  Writeln('GetModuleName(HInstance) in the host is the host program: ', SameText(GetModuleName(HInstance), ParamStr(0)));

  Say := TSay(GetProcAddress(H, 'Say'));
  Flush(Output);
  Say();
  Writeln('freed: ', FreeLibrary(H));
  Writeln('unloaded: ', GetModuleHandleW('lifelib.dll') = 0);
  PrintOrder('after FreeLibrary');
end.
