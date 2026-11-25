// covers: RTL-1065, RTL-1061
// phase: 8
program Sys_MemoryManagerArena;
{ A replacing manager installed at start-up (MemArenaKit, first in the uses clause) serves every request of the
  program from its static array: GetMem, AllocMem, ReallocMem, New, strings, dynamic arrays, objects,
  interface-counted objects, exceptions and a string list of Classes. Every address lies inside the array, and the
  default manager hands out and takes back nothing after the arena is installed. A block that came from elsewhere
  would end the program with a runtime error (the kit refuses it). }
uses
  MemArenaKit, SysUtils, Classes, TestKit;

type
  PPair = ^TPair;
  TPair = record
    A, B: Int64;
  end;

procedure RawBlocks;
var
  P: PByte;
  R: PPair;
  I: Integer;
  Zero: Boolean;
begin
  Section('GetMem, AllocMem, ReallocMem, New');
  GetMem(P, 100);
  Check(InArena(P), 'GetMem');
  P^ := 77;
  ReallocMem(P, 5000);
  Check(InArena(P) and (P^ = 77), 'ReallocMem growing keeps the bytes');
  ReallocMem(P, 10);
  Check(InArena(P) and (P^ = 77), 'ReallocMem shrinking keeps the bytes');
  FreeMem(P);
  P := AllocMem(64);
  Zero := True;
  for I := 0 to 63 do
    Zero := Zero and (PByte(NativeUInt(P) + NativeUInt(I))^ = 0);
  Check(InArena(P) and Zero, 'AllocMem');
  FreeMem(P);
  New(R);
  Check(InArena(R), 'New');
  Dispose(R);
end;

procedure ManagedValues;
var
  S, T: string;
  D: array of Integer;
  O: TObject;
  Counted: TInterfacedObject;
  Intf: IInterface;
  L: TStringList;
begin
  Section('strings and dynamic arrays');
  S := StringOfChar('a', 20);
  Check(InArena(Pointer(S)), 'a string');
  T := S + IntToStr(12345);
  Check(InArena(Pointer(T)) and (Length(T) = 25), 'a concatenation');
  SetLength(T, 2000);
  Check(InArena(Pointer(T)) and (T[25] = '5'), 'SetLength of a string');
  SetLength(D, 10);
  D[9] := 9;
  Check(InArena(Pointer(D)), 'a dynamic array');
  SetLength(D, 3000);
  Check(InArena(Pointer(D)) and (D[9] = 9), 'SetLength of a dynamic array again');

  Section('objects, interfaces, exceptions, Classes');
  O := TObject.Create;
  Check(InArena(Pointer(O)), 'an object');
  O.Free;
  Counted := TInterfacedObject.Create;
  Check(InArena(Pointer(Counted)), 'an interface-counted object');
  Intf := Counted;
  Intf := nil;
  try
    raise Exception.Create('raised ' + IntToStr(1));
  except
    on E: Exception do
      Check(InArena(Pointer(E)) and InArena(Pointer(E.Message)), 'a raised and handled exception');
  end;
  L := TStringList.Create;
  L.Add('line ' + IntToStr(1));
  L.Add('line ' + IntToStr(2));
  S := L[1];
  Check(InArena(Pointer(L)) and InArena(Pointer(S)) and (L.Count = 2), 'a string list');
  L.Free;
end;

begin
  RawBlocks;
  ManagedValues;
  Section('the default manager');
  Check(ArenaBlocks > 10, 'the arena served the requests');
  Check(IsMemoryManagerSet, 'the arena is the current manager');
  CheckEqI(LiveAtInit, _LiveAllocCount, 'the default manager handed out and took back nothing since the kit ran');
  Done;
end.
