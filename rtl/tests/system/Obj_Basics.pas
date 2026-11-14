// covers: RTL-150, RTL-151, RTL-152, ABI-070, ABI-072, ABI-077
program Obj_Basics;
{ The class functions of TObject (system.md section 13) on class names, class references and instances: ClassName,
  ClassNameIs (ASCII case-insensitive), ClassParent, ClassType, InstanceSize, InheritsFrom; and the instance
  set-up and tear-down routines called directly: NewInstance (zero-filled block, VMT pointer, one block),
  InitInstance, CleanupInstance (releases managed fields of every level, keeps the block), FreeInstance (releases
  fields and block), Free on nil and on an instance. Leak checks through the RTL-064 counter. }
{$POINTERMATH ON}
uses TestKit;

type
  TAnimal = class
    Name: string;
  end;

  TDog = class(TAnimal)
    Tricks: array of string;
    Age: Integer;
  end;

  TPuppy = class(TDog)
    Toy: string;
  end;

  TAnimalClass = class of TAnimal;

procedure TestNames;
var
  C: TClass;
  A: TAnimalClass;
begin
  Section('ClassName, ClassNameIs');
  CheckEqS('TObject', TObject.ClassName, 'TObject.ClassName');
  CheckEqS('TPuppy', TPuppy.ClassName, 'declared casing');
  CheckEqB(True, TPuppy.ClassNameIs('TPuppy'), 'ClassNameIs exact');
  CheckEqB(True, TPuppy.ClassNameIs('tpuppy'), 'ClassNameIs lower case');
  CheckEqB(True, TPuppy.ClassNameIs('TPUPPY'), 'ClassNameIs upper case');
  CheckEqB(False, TPuppy.ClassNameIs('TPupp'), 'ClassNameIs prefix');
  CheckEqB(False, TPuppy.ClassNameIs('TPuppyX'), 'ClassNameIs longer');
  CheckEqB(False, TPuppy.ClassNameIs('TDog'), 'ClassNameIs ancestor name');
  CheckEqB(False, TPuppy.ClassNameIs(''), 'ClassNameIs empty');
  C := TDog;
  CheckEqB(True, C.ClassNameIs('tdog'), 'ClassNameIs through TClass');
  A := TPuppy;
  CheckEqS('TPuppy', A.ClassName, 'ClassName through class of TAnimal');
end;

procedure TestHierarchy;
var
  C: TClass;
  N: Integer;
begin
  Section('ClassParent, InheritsFrom, InstanceSize');
  Check(TPuppy.ClassParent = TDog, 'TPuppy parent');
  Check(TDog.ClassParent = TAnimal, 'TDog parent');
  Check(TAnimal.ClassParent = TObject, 'TAnimal parent');
  Check(TObject.ClassParent = nil, 'TObject parent nil');
  N := 0;
  C := TPuppy;
  while C <> nil do
  begin
    N := N + 1;
    C := C.ClassParent;
  end;
  CheckEqI(4, N, 'depth of TPuppy');
  CheckEqB(True, TPuppy.InheritsFrom(TAnimal), 'TPuppy inherits TAnimal');
  CheckEqB(True, TPuppy.InheritsFrom(TObject), 'TPuppy inherits TObject');
  CheckEqB(False, TAnimal.InheritsFrom(TDog), 'TAnimal not from TDog');
  CheckEqI(8, TObject.InstanceSize, 'TObject 8');
  CheckEqI(16, TAnimal.InstanceSize, 'TAnimal 16');
  CheckEqI(32, TDog.InstanceSize, 'TDog 32');
  CheckEqI(40, TPuppy.InstanceSize, 'TPuppy 40');
end;

procedure TestInstances;
var
  O: TObject;
  Before: NativeInt;
begin
  Section('instances');
  Before := _LiveAllocCount;
  O := TPuppy.Create;
  Check(O.ClassType = TPuppy, 'ClassType');
  Check(O.ClassType.ClassParent = TDog, 'ClassType.ClassParent');
  CheckEqS('TPuppy', O.ClassName, 'ClassName on an instance');
  CheckEqB(True, O.ClassNameIs('TPUPPY'), 'ClassNameIs on an instance');
  CheckEqI(40, O.InstanceSize, 'InstanceSize on an instance');
  CheckEqB(True, O.InheritsFrom(TDog), 'InheritsFrom on an instance');
  O.Free;
  CheckEqI(0, _LiveAllocCount - Before, 'leaked');
end;

{ NewInstance and FreeInstance called directly, without a constructor }
procedure TestNewFreeInstance;
var
  O: TObject;
  P: TPuppy;
  I: Integer;
  Zero: Boolean;
  Before: NativeInt;
begin
  Section('NewInstance, FreeInstance');
  Before := _LiveAllocCount;
  O := TPuppy.NewInstance;
  CheckEqI(1, _LiveAllocCount - Before, 'one block');
  Check(PPointer(O)^ = Pointer(TPuppy), 'VMT pointer');
  Zero := True;
  for I := 8 to TPuppy.InstanceSize - 1 do
    if PByte(O)[I] <> 0 then
      Zero := False;
  CheckEqB(True, Zero, 'fields zero');
  P := TPuppy(O);
  P.Name := 'Rex' + P.Toy;
  P.Toy := P.Name + ' ball';
  SetLength(P.Tricks, 2);
  P.Tricks[1] := P.Toy + '!';
  Check(_LiveAllocCount - Before > 1, 'fields own blocks');
  O.FreeInstance;
  CheckEqI(0, _LiveAllocCount - Before, 'leaked after FreeInstance');
end;

{ CleanupInstance releases the managed fields of every level and leaves the block to the caller }
procedure TestCleanupInstance;
var
  P: TPuppy;
  Before: NativeInt;
begin
  Section('CleanupInstance');
  Before := _LiveAllocCount;
  P := TPuppy(TPuppy.NewInstance);
  P.Name := 'Max' + P.Toy;
  P.Toy := P.Name + ' rope';
  SetLength(P.Tricks, 3);
  P.Tricks[0] := P.Toy + '?';
  P.CleanupInstance;
  CheckEqI(1, _LiveAllocCount - Before, 'only the instance block remains');
  FreeMem(Pointer(P));
  CheckEqI(0, _LiveAllocCount - Before, 'leaked');
end;

procedure TestFree;
var
  O: TObject;
  D: TDog;
  Before: NativeInt;
begin
  Section('Free');
  O := nil;
  O.Free;
  D := nil;
  D.Free;
  Check(True, 'Free on nil does nothing');
  Before := _LiveAllocCount;
  D := TDog.Create;
  D.Name := 'Bo' + D.Name;
  D.Free;
  CheckEqI(0, _LiveAllocCount - Before, 'Free releases block and fields');
end;

begin
  TestNames;
  TestHierarchy;
  TestInstances;
  TestNewFreeInstance;
  TestCleanupInstance;
  TestFree;
  Done;
end.
