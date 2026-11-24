// covers: CMP-1090
program useown;

uses SysUtils;

function TextLength(Count: Integer): Integer; stdcall; external 'ownlib.dll';
function LibraryIsLibrary: Integer; stdcall; external 'ownlib.dll';
function LibraryLiveBlocks: NativeInt; stdcall; external 'ownlib.dll';

var
  Before, BeforeKept, LibraryBefore: NativeInt;
  Length1000: Integer;
  Kept: string;
begin
  Before := _LiveAllocCount;
  LibraryBefore := LibraryLiveBlocks;
  Length1000 := TextLength(1000);
  Writeln('host blocks unchanged by the call: ', _LiveAllocCount = Before);
  Writeln('library blocks back to the start: ', LibraryLiveBlocks = LibraryBefore);
  Writeln('TextLength(1000) = ', Length1000);

  // the counters are per module: a block of the host does not show on the library side
  BeforeKept := _LiveAllocCount;
  Kept := StringOfChar('y', 100) + IntToStr(Length1000);
  Writeln('host block counted by the host: ', _LiveAllocCount > BeforeKept);
  Writeln('not by the library: ', LibraryLiveBlocks = LibraryBefore);
  Writeln('kept ', Length(Kept), ' characters');

  Writeln('host IsLibrary: ', IsLibrary);
  Writeln('library IsLibrary: ', LibraryIsLibrary = 1);
end.
