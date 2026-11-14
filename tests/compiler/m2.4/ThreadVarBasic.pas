// covers: LANG-345, ABI-180, ABI-181, CMP-414
program ThreadVarBasic;
type
  PJob = ^TJob;
  TJob = record
    Step: Integer;
    Before: Integer;
    After: Integer;
    Wide: Int64;
    HistSum: Integer;
  end;
  TThreadStartRoutine = function(Parameter: Pointer): Cardinal; stdcall;

const
  INFINITE = $FFFFFFFF;

function CreateThread(Attributes: Pointer; StackSize: NativeUInt; StartAddress: TThreadStartRoutine;
  Parameter: Pointer; CreationFlags: Cardinal; ThreadId: Pointer): Pointer; stdcall; external 'kernel32.dll';
function WaitForSingleObject(Handle: Pointer; Milliseconds: Cardinal): Cardinal; stdcall; external 'kernel32.dll';
function CloseHandle(Handle: Pointer): LongBool; stdcall; external 'kernel32.dll';

threadvar
  Counter: Integer;
  Flag: Byte;
  Total: Int64;
  Hist: array[0..3] of Integer;

var
  Jobs: array[0..1] of TJob;
  H1, H2: Pointer;
  I: Integer;

procedure Bump(var V: Integer; N: Integer);
begin
  V := V + N;
end;

// every thread sees its own zero-initialised copies (LANG-345)
function Worker(Parameter: Pointer): Cardinal;
var
  Job: PJob;
  K: Integer;
  P: ^Integer;
begin
  Job := Parameter;
  Job^.Before := Counter + Flag + Hist[3];
  for K := 1 to 1000 do
    Inc(Counter, Job^.Step);
  P := @Counter;
  P^ := P^ + 1;
  Bump(Counter, 10);
  Flag := 7;
  Total := Int64(Counter) * 3;
  Hist[Job^.Step] := Job^.Step;
  Job^.After := Counter;
  Job^.Wide := Total + Flag;
  Job^.HistSum := Hist[0] + Hist[1] + Hist[2] + Hist[3];
  Result := 0;
end;

begin
  Writeln(Counter, ' ', Flag, ' ', Total, ' ', Hist[3]);
  Counter := 5;
  Hist[3] := 9;
  Jobs[0].Step := 1;
  Jobs[1].Step := 2;
  H1 := CreateThread(nil, 0, Worker, @Jobs[0], 0, nil);
  H2 := CreateThread(nil, 0, Worker, @Jobs[1], 0, nil);
  WaitForSingleObject(H1, INFINITE);
  WaitForSingleObject(H2, INFINITE);
  CloseHandle(H1);
  CloseHandle(H2);
  for I := 0 to 1 do
    Writeln(Jobs[I].Step, ' ', Jobs[I].Before, ' ', Jobs[I].After, ' ', Jobs[I].Wide, ' ', Jobs[I].HistSum);
  Writeln(Counter, ' ', Flag, ' ', Total);
  Writeln(Hist[1], ' ', Hist[2], ' ', Hist[3]);
end.
