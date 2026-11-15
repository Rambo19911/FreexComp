// covers: RTL-063, RTL-064
program Sys_HeapThreads;
{ The default memory manager is thread-safe (system.md RTL-063): four threads allocate, fill, verify, resize and free
  blocks of many sizes and build strings at the same time as the main thread does the same. Every block keeps its
  own content (no two live blocks overlap), and the interlocked counters of RTL-064 balance exactly afterwards.
  The threads only write into their job records; the main thread checks after waiting, so nothing depends on
  timing. }
{$POINTERMATH ON}
uses TestKit;

const
  Infinite = $FFFFFFFF;
  ThreadCount = 4;
  Rounds = 20000;
  Slots = 16;

type
  PJob = ^TJob;
  TJob = record
    Index: Integer;
    Corrupt: Integer;
    TextLength: Integer;
  end;

function CloseHandle(hObject: THandle): LongBool; stdcall; external 'kernel32.dll' name 'CloseHandle';
function WaitForSingleObject(Handle: THandle; Milliseconds: Cardinal): Cardinal; stdcall;
  external 'kernel32.dll' name 'WaitForSingleObject';

var
  Jobs: array[0..ThreadCount] of TJob;              { the last one is the main thread's }
  Handles: array[0..ThreadCount - 1] of THandle;
  Waits: array[0..ThreadCount - 1] of Cardinal;
  Before, Live: NativeInt;
  K: Integer;
  Id: Cardinal;

{ Allocates and frees blocks in a ring of Slots; each block is filled with a byte derived from its owner, slot and
  round, checked before it is resized or freed. }
function Churn(Parameter: Pointer): Integer;
var
  Job: PJob;
  Blocks: array[0..Slots - 1] of PByte;
  Sizes: array[0..Slots - 1] of Integer;
  Marks: array[0..Slots - 1] of Byte;
  R, S, B, Size: Integer;
  Text: string;
begin
  Job := Parameter;
  for S := 0 to Slots - 1 do
    Blocks[S] := nil;
  Text := '';
  for R := 1 to Rounds do
  begin
    if R mod 64 = 0 then
      Text := Text + Chr(Ord('a') + Job^.Index);
    S := R mod Slots;
    if Blocks[S] <> nil then
    begin
      for B := 0 to Sizes[S] - 1 do
        if Blocks[S][B] <> Marks[S] then
          Inc(Job^.Corrupt);
      if R mod 3 = 0 then
      begin
        Size := Sizes[S] * 2 + 1;
        ReallocMem(Blocks[S], Size);
        for B := 0 to Sizes[S] - 1 do
          if Blocks[S][B] <> Marks[S] then
            Inc(Job^.Corrupt);
        Sizes[S] := Size;
        FillChar(Blocks[S]^, Size, Marks[S]);
        Continue;
      end;
      FreeMem(Blocks[S]);
      Blocks[S] := nil;
    end;
    Sizes[S] := 1 + (R * 7 + Job^.Index * 13) mod 700;
    Marks[S] := Byte(Job^.Index * 31 + S * 7 + R);
    GetMem(Blocks[S], Sizes[S]);
    FillChar(Blocks[S]^, Sizes[S], Marks[S]);
  end;
  for S := 0 to Slots - 1 do
    if Blocks[S] <> nil then
      FreeMem(Blocks[S]);
  Job^.TextLength := Length(Text);
  Result := 0;
end;

begin
  Section('four threads and the main thread use the heap at once');   { the first output allocates the buffer }
  Before := _LiveAllocCount;
  for K := 0 to ThreadCount do
  begin
    Jobs[K].Index := K;
    Jobs[K].Corrupt := 0;
    Jobs[K].TextLength := -1;
  end;
  for K := 0 to ThreadCount - 1 do
    Handles[K] := BeginThread(nil, 0, Churn, @Jobs[K], 0, Id);
  Churn(@Jobs[ThreadCount]);
  for K := 0 to ThreadCount - 1 do
  begin
    Waits[K] := WaitForSingleObject(Handles[K], Infinite);
    CloseHandle(Handles[K]);
  end;
  { counted before any check: the string temporaries of the main block live until its end }
  Live := _LiveAllocCount - Before;
  for K := 0 to ThreadCount - 1 do
  begin
    Check(Handles[K] <> 0, 'thread ' + IntStr(K) + ' started');
    CheckEqI(0, Waits[K], 'thread ' + IntStr(K) + ' ended');
  end;
  for K := 0 to ThreadCount do
  begin
    CheckEqI(0, Jobs[K].Corrupt, 'job ' + IntStr(K) + ': every block kept its content');
    CheckEqI(Rounds div 64, Jobs[K].TextLength, 'job ' + IntStr(K) + ': its string grew undisturbed');
  end;
  CheckEqI(0, Live, 'allocation counters balance after the threads ended');
  Done;
end.
