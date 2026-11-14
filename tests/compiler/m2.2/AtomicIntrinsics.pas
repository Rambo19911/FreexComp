// covers: ABI-172, CMP-412
program AtomicIntrinsics;
type
  PInteger = ^Integer;

const
  Infinite = $FFFFFFFF;

function CreateThread(Attributes: Pointer; StackSize: NativeUInt; Start, Param: Pointer; Flags: Cardinal;
  ThreadId: Pointer): NativeInt; stdcall; external 'kernel32.dll';
function WaitForSingleObject(Handle: NativeInt; Milliseconds: Cardinal): Cardinal; stdcall; external 'kernel32.dll';
function CloseHandle(Handle: NativeInt): Integer; stdcall; external 'kernel32.dll';

var
  I: Integer;
  Q: Int64;
  C: Cardinal;
  A, B: Integer;
  P, R: PInteger;
  Counter: Integer;
  H1, H2: NativeInt;

function Worker(Param: Pointer): Cardinal;
var
  K: Integer;
begin
  for K := 1 to 100000 do
    AtomicIncrement(Counter);
  Result := 0;
end;

begin
  I := 10;
  Writeln(AtomicIncrement(I), ' ', I);
  Writeln(AtomicIncrement(I, 5), ' ', I);
  Writeln(AtomicDecrement(I), ' ', I);
  Writeln(AtomicDecrement(I, 20), ' ', I);
  Writeln(AtomicExchange(I, 42), ' ', I);
  Writeln(AtomicCmpExchange(I, 7, 0), ' ', I);
  Writeln(AtomicCmpExchange(I, 7, 42), ' ', I);

  Q := 4294967295;
  Writeln(AtomicIncrement(Q), ' ', Q);
  Writeln(AtomicDecrement(Q, 4294967297), ' ', Q);
  Writeln(AtomicExchange(Q, 9223372036854775807), ' ', Q);
  Writeln(AtomicIncrement(Q), ' ', Q);
  Writeln(AtomicCmpExchange(Q, 1, Low(Int64)), ' ', Q);

  C := 0;
  Writeln(AtomicDecrement(C), ' ', C);

  P := @A;
  R := AtomicExchange(P, @B);
  Writeln(R = @A, ' ', P = @B);
  R := AtomicCmpExchange(P, nil, @A);
  Writeln(R = @B, ' ', P = @B);
  R := AtomicCmpExchange(P, nil, @B);
  Writeln(R = @B, ' ', P = nil);
  P := @A;
  R := AtomicIncrement(P);
  Writeln(NativeInt(R) - NativeInt(@A), ' ', NativeInt(P) - NativeInt(@A));
  Writeln(NativeInt(AtomicDecrement(P, 3)) - NativeInt(@A));

  Counter := 0;
  H1 := CreateThread(nil, 0, @Worker, nil, 0, nil);
  H2 := CreateThread(nil, 0, @Worker, nil, 0, nil);
  WaitForSingleObject(H1, Infinite);
  WaitForSingleObject(H2, Infinite);
  CloseHandle(H1);
  CloseHandle(H2);
  Writeln(Counter);
end.
