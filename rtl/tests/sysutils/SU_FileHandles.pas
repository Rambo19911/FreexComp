// covers: RTL-260
// opc-run: tempdir
program SU_FileHandles;
{ sysutils.md section 8.1: FileCreate (exclusive by default, or the given share mode), FileOpen (access from Mode and 3,
  share from Mode and $F0, share 0 = deny none per RTL-260), FileSeek (all three origins, -1 on error), FileClose, the
  fm*/fa*/fs* constants. The program runs in a fresh temporary directory.
  TODO(C-202): FileRead / FileWrite take untyped Buffer parameters; the bytes are moved with ReadFile / WriteFile here. }
uses
  Windows, SysUtils, TestKit;

const
  Name = 'handles.bin';

procedure Put(H: THandle; const Bytes: AnsiString);
var
  Done_: DWORD;
begin
  WriteFile(H, PAnsiChar(Bytes), Length(Bytes), Done_, nil);
end;

function Get(H: THandle; Count: Integer): AnsiString;
var
  Done_: DWORD;
begin
  SetLength(Result, Count);
  ReadFile(H, PAnsiChar(Result), Count, Done_, nil);
  SetLength(Result, Done_);
end;

procedure TestConstants;
begin
  Section('constants');
  CheckEqI(0, fmOpenRead, 'fmOpenRead');
  CheckEqI(1, fmOpenWrite, 'fmOpenWrite');
  CheckEqI(2, fmOpenReadWrite, 'fmOpenReadWrite');
  CheckEqI($10, fmShareExclusive, 'fmShareExclusive');
  CheckEqI($20, fmShareDenyWrite, 'fmShareDenyWrite');
  CheckEqI($30, fmShareDenyRead, 'fmShareDenyRead');
  CheckEqI($40, fmShareDenyNone, 'fmShareDenyNone');
  CheckEqI($10, faDirectory, 'faDirectory');
  CheckEqI($1FF, faAnyFile, 'faAnyFile');
  CheckEqI(2, fsFromEnd, 'fsFromEnd');
end;

procedure TestCreateSeek;
var
  H, H2: THandle;
begin
  Section('FileCreate, FileSeek, FileClose');
  H := FileCreate(Name);
  Check(H <> INVALID_HANDLE_VALUE, 'FileCreate');
  Put(H, 'hello world');
  CheckEqI(11, FileSeek(H, 0, fsFromCurrent), 'position after writing');
  CheckEqI(11, FileSeek(H, 0, fsFromEnd), 'fsFromEnd');
  CheckEqI(6, FileSeek(H, 6, fsFromBeginning), 'fsFromBeginning');
  CheckEqS('world', string(Get(H, 5)), 'read at the position');
  CheckEqI(8, FileSeek(H, -3, fsFromEnd), 'negative offset from the end');
  CheckEqI(-1, FileSeek(H, -100, fsFromBeginning), 'before the start: -1');
  H2 := FileOpen(Name, fmOpenRead);
  Check(H2 = INVALID_HANDLE_VALUE, 'FileCreate is exclusive by default');
  FileClose(H);
  CheckEqI(-1, FileSeek(INVALID_HANDLE_VALUE, 0, fsFromBeginning), 'invalid handle: -1');
  H := FileCreate(Name, fmShareDenyWrite);
  Check(H <> INVALID_HANDLE_VALUE, 'FileCreate with a share mode truncates');
  CheckEqI(0, FileSeek(H, 0, fsFromEnd), 'empty after FileCreate');
  H2 := FileOpen(Name, fmOpenRead);
  Check(H2 <> INVALID_HANDLE_VALUE, 'deny write allows another reader');
  FileClose(H2);
  H2 := FileOpen(Name, fmOpenWrite);
  Check(H2 = INVALID_HANDLE_VALUE, 'deny write refuses another writer');
  Put(H, 'abc');
  FileClose(H);
end;

procedure TestOpen;
var
  H, H2: THandle;
begin
  Section('FileOpen');
  Check(FileOpen('missing.bin', fmOpenRead) = INVALID_HANDLE_VALUE, 'missing file');
  H := FileOpen(Name, fmOpenRead);
  Check(H <> INVALID_HANDLE_VALUE, 'fmOpenRead');
  CheckEqS('abc', string(Get(H, 10)), 'contents');
  H2 := FileOpen(Name, fmOpenRead);
  Check(H2 <> INVALID_HANDLE_VALUE, 'share 0 means deny none (RTL-260)');
  FileClose(H2);
  FileClose(H);
  H := FileOpen(Name, fmOpenRead or fmShareExclusive);
  Check(H <> INVALID_HANDLE_VALUE, 'exclusive open');
  Check(FileOpen(Name, fmOpenRead) = INVALID_HANDLE_VALUE, 'exclusive refuses a second open');
  FileClose(H);
  H := FileOpen(Name, fmOpenReadWrite or fmShareDenyNone);
  Check(H <> INVALID_HANDLE_VALUE, 'fmOpenReadWrite');
  FileSeek(H, 0, fsFromEnd);
  Put(H, 'def');
  FileSeek(H, 0, fsFromBeginning);
  CheckEqS('abcdef', string(Get(H, 10)), 'read and write');
  FileClose(H);
  H := FileOpen(Name, fmOpenWrite);
  CheckEqS('', string(Get(H, 10)), 'fmOpenWrite cannot read');
  FileClose(H);
end;

begin
  TestConstants;
  TestCreateSeek;
  TestOpen;
  Done;
end.
