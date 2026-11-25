// phase: 8
// covers: RTL-1050, RTL-1051
program Win_ResourceSmoke;
{ Windows unit, resources: FindResourceW / FindResource, LoadResource, LockResource and SizeofResource on the
  application manifest of the program and on the version resource of a system library loaded as a data file; the
  numbers of the resource type identifiers. Sizes are checked as greater than 0, never printed. }
uses Windows, TestKit;

{$POINTERMATH ON}

function IsType(T: PWideChar; Number: Integer): Boolean;
begin
  Result := NativeUInt(T) = NativeUInt(Number);
end;

{ True when the Len bytes at P contain the single-byte text Text }
function ContainsText(P: PByte; Len: Integer; const Text: string): Boolean;
var
  I, J: Integer;
begin
  Result := False;
  for I := 0 to Len - Length(Text) do
  begin
    J := 0;
    while (J < Length(Text)) and (P[I + J] = Ord(Text[J + 1])) do
      Inc(J);
    if J = Length(Text) then
      Exit(True);
  end;
end;

procedure TestTypes;
begin
  Section('resource type identifiers (RTL-1051)');
  Check(IsType(RT_CURSOR, 1), 'RT_CURSOR');
  Check(IsType(RT_BITMAP, 2), 'RT_BITMAP');
  Check(IsType(RT_ICON, 3), 'RT_ICON');
  Check(IsType(RT_MENU, 4), 'RT_MENU');
  Check(IsType(RT_DIALOG, 5), 'RT_DIALOG');
  Check(IsType(RT_STRING, 6), 'RT_STRING');
  Check(IsType(RT_FONTDIR, 7), 'RT_FONTDIR');
  Check(IsType(RT_FONT, 8), 'RT_FONT');
  Check(IsType(RT_ACCELERATOR, 9), 'RT_ACCELERATOR');
  Check(IsType(RT_RCDATA, 10), 'RT_RCDATA');
  Check(IsType(RT_MESSAGETABLE, 11), 'RT_MESSAGETABLE');
  Check(IsType(RT_GROUP_CURSOR, 12), 'RT_GROUP_CURSOR');
  Check(IsType(RT_GROUP_ICON, 14), 'RT_GROUP_ICON');
  Check(IsType(RT_VERSION, 16), 'RT_VERSION');
  Check(IsType(RT_DLGINCLUDE, 17), 'RT_DLGINCLUDE');
  Check(IsType(RT_PLUGPLAY, 19), 'RT_PLUGPLAY');
  Check(IsType(RT_VXD, 20), 'RT_VXD');
  Check(IsType(RT_ANICURSOR, 21), 'RT_ANICURSOR');
  Check(IsType(RT_ANIICON, 22), 'RT_ANIICON');
  Check(IsType(RT_HTML, 23), 'RT_HTML');
  Check(IsType(RT_MANIFEST, 24), 'RT_MANIFEST');
end;

procedure TestOwnManifest;
var
  Info, Other: HRSRC;
  Data: HGLOBAL;
  P: PByte;
  Size: DWORD;
begin
  Section('the manifest of the program (RTL-1050)');
  Info := FindResourceW(HInstance, MakeIntResource(1), RT_MANIFEST);
  Check(Info <> 0, 'FindResourceW');
  Other := FindResource(HInstance, MakeIntResource(1), RT_MANIFEST);
  Check(Other = Info, 'FindResource is the same function');
  Other := FindResourceW(0, '#1', '#24');
  Check(Other = Info, 'module 0 and the #number forms find the same resource');
  Data := LoadResource(HInstance, Info);
  Check(Data <> 0, 'LoadResource');
  P := LockResource(Data);
  Check(P <> nil, 'LockResource');
  Size := SizeofResource(HInstance, Info);
  Check(Size > 0, 'SizeofResource > 0');
  Check(ContainsText(P, Size, 'assembly'), 'the bytes are text that contains assembly');
  Check(FindResourceW(HInstance, 'NOPE', RT_RCDATA) = 0, 'a missing resource gives 0');
end;

procedure TestSystemLibrary;
var
  Lib: HMODULE;
  Info: HRSRC;
  Data: HGLOBAL;
  P: PByte;
  Size: DWORD;
begin
  Section('the version resource of a system library (RTL-1050)');
  Lib := LoadLibraryExW('kernel32.dll', 0, LOAD_LIBRARY_AS_DATAFILE or LOAD_LIBRARY_SEARCH_SYSTEM32);
  Check(Lib <> 0, 'the library is loaded as a data file');
  Info := FindResourceW(Lib, MakeIntResource(1), RT_VERSION);
  Check(Info <> 0, 'FindResourceW');
  Data := LoadResource(Lib, Info);
  Check(Data <> 0, 'LoadResource');
  P := LockResource(Data);
  Check(P <> nil, 'LockResource');
  Size := SizeofResource(Lib, Info);
  Check(Size > 0, 'SizeofResource > 0');
  { a version resource starts with its own length in a 16-bit word }
  Check((Size >= 2) and (PWord(P)^ > 0) and (PWord(P)^ <= Size), 'the resource starts with its length');
  Check(FindResourceW(Lib, MakeIntResource(1), RT_HTML) = 0, 'the library has no such resource of another type');
  Check(FreeLibrary(Lib), 'FreeLibrary');
end;

begin
  TestTypes;
  TestOwnManifest;
  TestSystemLibrary;
  Done;
end.
