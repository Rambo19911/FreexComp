// covers: CMP-1328
program ResLanguageNames;

{ C-349: a script names its languages with LANG_* / SUBLANG_* and computes an identifier with MAKELANGID; the program finds each
  entry under the language identifier the constants stand for (1033 English - United States, 1031 German - Germany) and the
  string whose number is the identifier of Latvian - Latvia (1062). The two system functions are looked up at run time. }

{$R *.rc}

uses
  Windows;

type
  TFindResourceEx = function(hModule: HMODULE; lpType, lpName: LPCWSTR; wLanguage: Word): HRSRC; stdcall;
  TLoadString = function(hInstance: HMODULE; uID: Cardinal; lpBuffer: LPWSTR; cchBufferMax: Integer): Integer; stdcall;

var
  FindResourceEx: TFindResourceEx;
  LoadString: TLoadString;

procedure Show(Id: Word; Language: Word);
begin
  Writeln(Id, ' in language ', Language, ': ', FindResourceEx(HMODULE(HInstance), RT_RCDATA, MakeIntResource(Id), Language) <> 0);
end;

var
  Buffer: array[0..15] of WideChar;
  Count: Integer;
begin
  FindResourceEx := TFindResourceEx(GetProcAddress(GetModuleHandleW('kernel32.dll'), 'FindResourceExW'));
  LoadString := TLoadString(GetProcAddress(LoadLibraryW('user32.dll'), 'LoadStringW'));
  Show(1, 1033);
  Show(1, 1031);
  Show(2, 1031);
  Show(2, 1033);
  Count := LoadString(HMODULE(HInstance), 1062, @Buffer[0], Length(Buffer));
  Writeln('string 1062: ', Count, ' ', string(PWideChar(@Buffer[0])));
end.
