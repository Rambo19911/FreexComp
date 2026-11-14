// phase: 4
// covers: ABI-150, ABI-151
program Sys_Resources;
{ The resource registry of ABI-150/151: _RegisterResources keeps (Table, Count) pairs without copying them;
  FindResourceData matches the name ASCII case-insensitively, returns the registered Data/Size, the first
  registration of a name wins, and an absent name gives False, nil and 0. The tables are built here by hand
  (the compiler golden m2.5/FormEmbed covers the tables the compiler emits for the FORM directive). }
uses TestKit;

type
  TEntry = record
    Name: Pointer;
    Data: Pointer;
    Size: NativeInt;
  end;

var
  First: array[0..1] of TEntry;
  Second: array[0..1] of TEntry;
  NA, NB, NC: string;
  BytesA: array[0..3] of Byte;
  BytesB: array[0..1] of Byte;
  BytesC: array[0..0] of Byte;
  D: Pointer;
  S: NativeInt;

begin
  Section('empty and absent');
  D := @BytesA;
  S := 5;
  CheckEqB(False, FindResourceData('TNOTHING', D, S), 'absent before registration');
  Check(D = nil, 'absent gives nil');
  CheckEqI(0, S, 'absent gives size 0');

  NA := 'TFORMA';
  NB := 'TFORMB';
  NC := 'TFORMA';
  First[0].Name := Pointer(NA); First[0].Data := @BytesA; First[0].Size := 4;
  First[1].Name := Pointer(NB); First[1].Data := @BytesB; First[1].Size := 2;
  Second[0].Name := Pointer(NC); Second[0].Data := @BytesC; Second[0].Size := 1;
  Second[1].Name := Pointer(NB); Second[1].Data := @BytesC; Second[1].Size := 1;
  _RegisterResources(@First, 2);
  _RegisterResources(@Second, 2);
  _RegisterResources(nil, 0);

  Section('lookup');
  CheckEqB(True, FindResourceData('TFORMA', D, S), 'found');
  Check(D = @BytesA, 'data of the first registration');
  CheckEqI(4, S, 'size of the first registration');
  CheckEqB(True, FindResourceData('tFormB', D, S), 'case-insensitive');
  Check(D = @BytesB, 'first registration wins over a later table');
  CheckEqI(2, S, 'size');
  CheckEqB(False, FindResourceData('TFORM', D, S), 'prefix is not a match');
  CheckEqB(False, FindResourceData('TFORMAB', D, S), 'longer name is not a match');
  CheckEqB(False, FindResourceData('', D, S), 'empty name');
  Check(D = nil, 'nil after a miss');
  CheckEqI(0, S, 'size 0 after a miss');

  Section('tables are not copied');
  First[0].Size := 3;
  CheckEqB(True, FindResourceData('TFORMA', D, S), 'still found');
  CheckEqI(3, S, 'reads the registered table in place');
  Done;
end.
