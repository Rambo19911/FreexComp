// covers: RTL-959, RTL-965
program usethreads;
// four threads of the host touch a threadvar and IOResult of the library, wait until the host has counted the
// library's blocks, and end: the thread-detach notification gives the blocks back. Events and joins only, no timing.

uses Windows;

const
  ThreadCount = 4;

function Touch(Index: Integer): Integer; stdcall; external 'threadlib.dll';
function LiveBlocks: NativeInt; stdcall; external 'threadlib.dll';

var
  Touched: array[0..ThreadCount - 1] of THandle;
  Threads: array[0..ThreadCount - 1] of THandle;
  First, Second: array[0..ThreadCount - 1] of Integer;
  Release: THandle;

function Worker(Parameter: Pointer): Integer;
var
  Index: Integer;
begin
  Index := Integer(NativeInt(Parameter));
  First[Index] := Touch(Index);
  Second[Index] := Touch(Index);
  SetEvent(Touched[Index]);
  WaitForSingleObject(Release, INFINITE);
  Result := 0;
end;

var
  I: Integer;
  Id: Cardinal;
  LibraryStart, HostStart: NativeInt;
begin
  Writeln('main thread: ', Touch(10));
  LibraryStart := LiveBlocks;
  HostStart := _LiveAllocCount;
  Release := CreateEventW(nil, True, False, nil);
  for I := 0 to ThreadCount - 1 do
  begin
    Touched[I] := CreateEventW(nil, False, False, nil);
    Threads[I] := BeginThread(nil, 0, @Worker, Pointer(NativeInt(I)), 0, Id);
  end;
  for I := 0 to ThreadCount - 1 do
    WaitForSingleObject(Touched[I], INFINITE);
  for I := 0 to ThreadCount - 1 do
    Writeln('thread ', I, ': ', First[I], ' then ', Second[I]);
  Writeln('library blocks held by the ', ThreadCount, ' threads: ', LiveBlocks - LibraryStart);

  SetEvent(Release);
  for I := 0 to ThreadCount - 1 do
  begin
    WaitForSingleObject(Threads[I], INFINITE);
    CloseHandle(Threads[I]);
    CloseHandle(Touched[I]);
  end;
  CloseHandle(Release);
  Writeln('library blocks after the threads have ended: ', LiveBlocks - LibraryStart);
  Writeln('host blocks back to the start: ', _LiveAllocCount = HostStart);
  Writeln('main thread again: ', Touch(10));
end.
