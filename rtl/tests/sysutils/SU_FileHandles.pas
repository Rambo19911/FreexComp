// covers: RTL-260
// opc-run: tempdir
program SU_FileHandles;
{ sysutils.md section 8.1: FileCreate (exclusive by default, or the given share mode), FileOpen (access from Mode and 3,
  share from Mode and $F0, share 0 = deny none per RTL-260), FileSeek (all three origins, -1 on error), FileClose, the
  fm*/fa*/fs* constants, FileRead / FileWrite (untyped buffers; counts, -1 on error). The program runs in a fresh
  temporary directory. }
uses
  Windows, SysUtils, TestKit;

const
  Name = 'handles.bin';

procedure Put(H: THandle; const Bytes: AnsiString);
begin
  CheckEqI(Length(Bytes), FileWrite(H, PAnsiChar(Bytes)^, Length(Bytes)), 'FileWrite count ' + string(Bytes));
end;

function Get(H: THandle; Count: Integer): AnsiString;
var
  Got: Integer;
begin
  SetLength(Result, Count);
  Got := FileRead(H, Result[1], Count);
  if Got < 0 then
    Got := 0;
  SetLength(Result, Got);
end;

procedure TestReadWrite;
var
  H: THandle;
  N: Integer;
  W: Word;
  Buf: array[0..3] of Byte;
begin
  Section('FileRead, FileWrite');
  H := FileCreate('rw.bin');
  N := $04030201;
  CheckEqI(4, FileWrite(H, N, SizeOf(N)), 'FileWrite of an Integer variable');
  W := $0605;
  CheckEqI(2, FileWrite(H, W, SizeOf(W)), 'FileWrite of a Word variable');
  CheckEqI(0, FileWrite(H, N, 0), 'FileWrite of 0 bytes');
  FileSeek(H, 0, fsFromBeginning);
  CheckEqI(4, FileRead(H, Buf, SizeOf(Buf)), 'FileRead into an array');
  CheckEqI(1, Buf[0], 'byte 0');
  CheckEqI(4, Buf[3], 'byte 3');
  W := 0;
  CheckEqI(2, FileRead(H, W, 4), 'FileRead stops at the end');
  CheckEqI($0605, W, 'the Word read back');
  CheckEqI(0, FileRead(H, W, 2), 'FileRead at the end gives 0');
  FileClose(H);
  CheckEqI(-1, FileRead(INVALID_HANDLE_VALUE, Buf, 4), 'FileRead on an invalid handle');
  CheckEqI(-1, FileWrite(INVALID_HANDLE_VALUE, Buf, 4), 'FileWrite on an invalid handle');
  H := FileOpen('rw.bin', fmOpenRead);
  CheckEqI(-1, FileWrite(H, Buf, 4), 'FileWrite on a read-only handle');
  FileClose(H);
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
  TestReadWrite;
  Done;
end.
