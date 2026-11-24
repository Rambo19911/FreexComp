// covers: CMP-1090
library ownlib;
// the library has its own run-time library: its strings live and die on its side

uses SysUtils;

function TextLength(Count: Integer): Integer; stdcall;
var
  S: string;
begin
  S := StringOfChar('x', Count) + IntToStr(Count);
  Result := Length(S);
end;

function LibraryIsLibrary: Integer; stdcall;
begin
  Result := Ord(IsLibrary);
end;

function LibraryLiveBlocks: NativeInt; stdcall;
begin
  Result := _LiveAllocCount;
end;

exports
  TextLength,
  LibraryIsLibrary,
  LibraryLiveBlocks;

end.
