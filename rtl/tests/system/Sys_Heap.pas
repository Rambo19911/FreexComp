// covers: RTL-060, RTL-061
// opc-run: variants 1 2
program Sys_Heap;
{$POINTERMATH ON}
{ Variant 1: allocation behaviour. Variant 2: an impossible request is runtime error 203.
  The variant is the last character of the command line (ParamStr arrives with R-011). }
// TODO(C-072): call System's AllocMem and _LiveAllocCount directly; a call from the program to an ordinary System
// routine is emitted without its "declare" today, so the link names are declared here and resolved by the linker.
function AllocMem(Size: NativeInt): Pointer; external name 'System.AllocMem';
function _LiveAllocCount: NativeInt; external name 'System._LiveAllocCount';

type
  TPair = record
    A, B: Int64;
    C: array[0..9] of Byte;
  end;
  PPair = ^TPair;

var
  P, Q: PByte;
  R: PPair;
  Blocks: array[0..99] of PByte;
  Bits: PNativeUInt;
  Cmd: PChar;
  I, Bad, Misaligned: Integer;
  Baseline: NativeInt;

{ the address bits of P (no typecasts before C-057) }
function Address(var P: PByte): NativeUInt;
begin
  Bits := @P;
  Result := Bits^;
end;

procedure CheckPattern(Count: Integer);
var
  K: Integer;
begin
  Bad := 0;
  for K := 0 to Count - 1 do
    if P[K] <> K mod 251 then
      Bad := Bad + 1;
  Writeln(' bad bytes ', Bad);
end;

begin
  Cmd := CmdLine;
  while Cmd^ <> #0 do
    Inc(Cmd);
  Dec(Cmd);
  if Cmd^ = '2' then
  begin
    Writeln('asking for 2^62 bytes');
    GetMem(P, $4000000000000000);
    Writeln('not reached');
  end;

  Baseline := _LiveAllocCount;

  GetMem(P, 0);
  Writeln('GetMem 0 is nil ', P = nil);
  FreeMem(P);
  Writeln('FreeMem nil is a no-op');
  Writeln('AllocMem 0 is nil ', AllocMem(0) = nil);
  Writeln('AllocMem -1 is nil ', AllocMem(-1) = nil);

  GetMem(P, 300);
  for I := 0 to 299 do
    P[I] := I mod 251;
  ReallocMem(P, 100000);
  Write('grown');
  CheckPattern(300);
  ReallocMem(P, 40);
  Write('shrunk');
  CheckPattern(40);
  Writeln('live blocks +1 ', _LiveAllocCount - Baseline);
  ReallocMem(P, 0);
  Writeln('ReallocMem 0 frees ', P = nil, ' ', _LiveAllocCount - Baseline);
  ReallocMem(P, 16);
  Writeln('ReallocMem nil allocates ', P <> nil, ' ', _LiveAllocCount - Baseline);
  FreeMem(P);

  Q := AllocMem(5000);
  Bad := 0;
  for I := 0 to 4999 do
    if Q[I] <> 0 then
      Bad := Bad + 1;
  Writeln('AllocMem nonzero bytes ', Bad);
  FreeMem(Q);

  New(R);
  Writeln('New is zeroed ', (R^.A = 0) and (R^.B = 0) and (R^.C[9] = 0));
  Dispose(R);

  Misaligned := 0;
  for I := 0 to 99 do
  begin
    GetMem(Blocks[I], 1 + I * 7);
    if Address(Blocks[I]) mod 16 <> 0 then
      Misaligned := Misaligned + 1;
  end;
  Writeln('live blocks +100 ', _LiveAllocCount - Baseline);
  for I := 0 to 99 do
    FreeMem(Blocks[I]);
  Writeln('misaligned blocks ', Misaligned);
  Writeln('live blocks back to baseline ', _LiveAllocCount - Baseline);
end.
