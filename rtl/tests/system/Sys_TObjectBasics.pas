// covers: ABI-070, ABI-072, ABI-073, ABI-075, ABI-076, ABI-077, RTL-150, RTL-151, RTL-152
program Sys_TObjectBasics;
{ TObject of rtl/src/System.pas: create/free of TObject itself, the virtual destructor chain through a TObject
  reference (BeforeDestruction outermost, then each Destroy, then FreeInstance), managed fields of every level
  released by FreeInstance (no block left, RTL-064 counters), Free on nil, NewInstance zero-filling a block that the
  memory manager hands out dirty, the VMT pointer stored at offset 0, InstanceSize and the default virtuals. }
{$POINTERMATH ON}
uses TestKit;

type
  TInts = array of Integer;

  TBase = class
    Title: string;
    Values: TInts;
    constructor Create;
    destructor Destroy; override;
    procedure AfterConstruction; override;
    procedure BeforeDestruction; override;
    procedure FreeInstance; override;
  end;

  TLeaf = class(TBase)
    Count: Integer;
    Tags: array of string;
    Note: string;
    destructor Destroy; override;
  end;

  TPlain = class
    A: Int64;
    B: Integer;
    C: string;
    D: Pointer;
  end;

var
  Log: string;

constructor TBase.Create;
begin
  Log := Log + 'C';
end;

destructor TBase.Destroy;
begin
  Log := Log + 'D(base)';
  inherited Destroy;
end;

procedure TBase.AfterConstruction;
begin
  Log := Log + 'A';
end;

procedure TBase.BeforeDestruction;
begin
  Log := Log + 'B';
end;

procedure TBase.FreeInstance;
begin
  Log := Log + 'F';
  inherited FreeInstance;
end;

destructor TLeaf.Destroy;
begin
  Log := Log + 'D(leaf)';
  inherited Destroy;
end;

{ InstanceSize from the VMT (class method calls such as TPlain.InstanceSize wait for compiler C-086) }
function VmtSize(O: TObject): NativeInt;
begin
  Result := PNativeInt(PByte(PPointer(O)^) + 8)^;
end;

{ a memory manager whose GetMem hands out blocks filled with $A5, so zero bytes come from NewInstance (ABI-072) }
var
  Saved: TMemoryManager;

function DirtyGetMem(Size: NativeInt): Pointer;
var
  P: PByte;
  I: NativeInt;
begin
  P := Saved.GetMem(Size);
  for I := 0 to Size - 1 do
    P[I] := $A5;
  Result := P;
end;

procedure TestCreateFree;
var
  O: TObject;
  Before: NativeInt;
begin
  Section('TObject itself');
  Before := _LiveAllocCount;
  O := TObject.Create;
  Check(O <> nil, 'Create returns an instance');
  CheckEqI(Before + 1, _LiveAllocCount, 'one block per instance');
  Check(PPointer(O)^ <> nil, 'VMT pointer stored at offset 0');
  CheckEqI(8, VmtSize(O), 'TObject InstanceSize (VMT+8)');
  CheckEqS('TObject', O.ToString, 'ToString = ClassName');
  CheckEqB(True, O.Equals(O), 'Equals self');
  CheckEqB(False, O.Equals(nil), 'Equals nil');
  CheckEqI(Integer(NativeInt(O) shr 4), O.GetHashCode, 'GetHashCode = address shr 4');
  O.Free;
  CheckEqI(0, _LiveAllocCount - Before, 'leaked after Free');
end;

procedure TestDestructorChain;
var
  O: TObject;
  Before: NativeInt;
begin
  Section('virtual destructor chain');
  Before := _LiveAllocCount;
  Log := '';
  O := TLeaf.Create;
  CheckEqS('CA', Log, 'construction order');
  CheckEqS('TLeaf', O.ToString, 'ToString of a descendant');
  Log := '';
  O.Free;
  CheckEqS('BD(leaf)D(base)F', Log, 'Free through a TObject reference');
  O := TBase.Create;
  Log := '';
  O.Destroy;
  CheckEqS('BD(base)F', Log, 'Destroy through a TObject reference');
  Log := '';                           { the log's own block }
  CheckEqI(0, _LiveAllocCount - Before, 'leaked');
end;

{ in a routine of its own: compiler temporaries (here of 'tag' + Chr(...)) are released when their routine ends }
procedure FillTags(L: TLeaf);
var
  I: Integer;
begin
  SetLength(L.Tags, 3);
  for I := 0 to 2 do
    L.Tags[I] := 'tag' + Chr(Ord('A') + I);
end;

procedure TestManagedFields;
var
  L: TLeaf;
  Before: NativeInt;
begin
  Section('managed fields');
  Before := _LiveAllocCount;
  L := TLeaf.Create;
  CheckEqB(True, L.Title = '', 'string field starts empty');
  CheckEqI(0, Length(L.Values), 'dynamic array field starts empty');
  CheckEqI(0, Length(L.Tags), 'string array field starts empty');
  L.Title := 'base';
  L.Title := L.Title + ' title';
  SetLength(L.Values, 4);
  FillTags(L);
  CheckEqS('tagC', L.Tags[2], 'string array element');
  L.Note := L.Title + '!';
  L.Count := 3;
  CheckEqS('base title!', L.Note, 'fields usable');
  Check(_LiveAllocCount - Before > 1, 'fields own blocks');
  L.Free;
  Log := '';                           { the log's own block }
  CheckEqI(0, _LiveAllocCount - Before, 'leaked (base and leaf InitTables walked)');
end;

procedure TestFreeNil;
var
  O: TObject;
  B: TBase;
begin
  Section('Free on nil');
  O := nil;
  O.Free;
  B := nil;
  Log := '';
  B.Free;
  CheckEqS('', Log, 'no destructor runs');
end;

procedure TestZeroFill;
var
  Dirty: TMemoryManager;
  P: TPlain;
  Before: NativeInt;
  I: Integer;
  Zero: Boolean;
begin
  Section('NewInstance zero-fills');
  GetMemoryManager(Saved);
  Dirty := Saved;
  Dirty.GetMem := DirtyGetMem;
  Before := _LiveAllocCount;
  SetMemoryManager(Dirty);
  P := TPlain.Create;
  SetMemoryManager(Saved);
  CheckEqI(40, VmtSize(P), 'TPlain InstanceSize (VMT+8)');
  Zero := True;
  for I := 8 to VmtSize(P) - 1 do
    if PByte(P)[I] <> 0 then
      Zero := False;
  CheckEqB(True, Zero, 'every byte after the VMT pointer is zero');
  CheckEqI(0, P.A, 'Int64 field');
  CheckEqI(0, P.B, 'Integer field');
  CheckEqB(True, P.C = '', 'string field');
  Check(P.D = nil, 'Pointer field');
  P.C := 'x' + P.C + 'y';
  P.Free;
  CheckEqI(0, _LiveAllocCount - Before, 'leaked');
end;

begin
  TestCreateFree;
  TestDestructorChain;
  TestManagedFields;
  TestFreeNil;
  TestZeroFill;
  Done;
end.
