// covers: LANG-611, LANG-640, ABI-140, ABI-141, ABI-142, CMP-406, CMP-407
program RttiPublished;

{ The published properties of a class and its ancestors, enumerated from the class RTTI blocks (VMT+24, ABI section
  10.1, raw pointer walk) and read and written through their TPropInfo entries: field accessors (GetKind/SetKind 1,
  Getter = field offset), static method accessors (2, a code address; with an Index argument after Self) and a
  virtual getter (3, a VMT offset, so the override runs). Members before the first visibility keyword are published
  in a class whose ancestor was declared under $M+. Public properties are not published (Visibility 2). }

{$POINTERMATH ON}

type
  TColor = (Red, Green, Blue);

  {$M+}
  TBase = class
  private
    FName: string;
    FCount: Integer;
    FColor: TColor;
    function GetTwice: Integer;
    procedure SetCount(Value: Integer);
    function GetSlot(Index: Integer): Integer;
  protected
    function GetVirt: Integer; virtual;
  published
    property Name: string read FName write FName;
    property Count: Integer read FCount write SetCount;
    property Twice: Integer read GetTwice;
    property Virt: Integer read GetVirt;
    property Slot3: Integer index 3 read GetSlot;
  end;
  {$M-}

  TChild = class(TBase)
    property Color: TColor read FColor write FColor;
  protected
    function GetVirt: Integer; override;
  public
    property Hidden: Integer read FCount;
  end;

  PStr = ^string;
  TGetter = function(Self: Pointer): Integer;
  TIndexedGetter = function(Self: Pointer; Index: Integer): Integer;
  TSetter = procedure(Self: Pointer; Value: Integer);

function TBase.GetTwice: Integer;
begin
  Result := FCount * 2;
end;

procedure TBase.SetCount(Value: Integer);
begin
  Writeln('SetCount ', Value);
  FCount := Value;
end;

function TBase.GetSlot(Index: Integer): Integer;
begin
  Result := Index * 100 + FCount;
end;

function TBase.GetVirt: Integer;
begin
  Result := 1;
end;

function TChild.GetVirt: Integer;
begin
  Result := 2;
end;

function At(P: Pointer; Offset: Integer): Pointer;
begin
  Result := PPointer(PByte(P) + Offset)^;
end;

function Str(P: Pointer): string;
begin
  if P = nil then
    Result := ''
  else
    Result := PChar(P);
end;

function ParentName(TI: Pointer): string;
begin
  if At(TI, 24) = nil then
    Result := '-'
  else
    Result := Str(At(At(TI, 24), 8));
end;

{ the value of an Integer-typed property through its getter }
function OrdinalValue(Obj: TObject; P: PByte): Integer;
var
  Code: Pointer;
begin
  Code := Pointer(PNativeInt(P + 24)^);
  case P[16] of
    1: Result := PInteger(PByte(Obj) + PNativeInt(P + 24)^)^;
    3: Result := TGetter(At(PPointer(Obj)^, PNativeInt(P + 24)^))(Pointer(Obj));
  else
    if PInteger(P + 48)^ <> Integer($80000000) then
      Result := TIndexedGetter(Code)(Pointer(Obj), PInteger(P + 48)^)
    else
      Result := TGetter(Code)(Pointer(Obj));
  end;
end;

procedure ShowProp(Obj: TObject; P: PByte);
var
  PropType: PByte;
begin
  PropType := At(P, 8);
  Write('  ', Str(At(P, 0)), ': ', Str(At(PropType, 8)), ' get ', P[16], ' set ', P[17], ' stored ', P[18], '/', PNativeInt(P + 40)^);
  if PInteger(P + 48)^ = Integer($80000000) then
    Write(' index none')
  else
    Write(' index ', PInteger(P + 48)^);
  Write(' default none ', PInt64(P + 56)^ = Low(Int64), ' = ');
  case PropType[0] of
    9: Writeln(PStr(PByte(Obj) + PNativeInt(P + 24)^)^);
    3: Writeln(Str(PPointer(At(PropType, 48))[(PByte(Obj) + PNativeInt(P + 24)^)^]));
  else
    Writeln(OrdinalValue(Obj, P));
  end;
end;

function FindProp(Obj: TObject; const Name: string): PByte;
var
  TI, P: PByte;
  I: Integer;
begin
  TI := At(PPointer(Obj)^, 24);
  while TI <> nil do
  begin
    for I := 0 to PNativeInt(TI + 40)^ - 1 do
    begin
      P := PByte(At(TI, 48)) + I * 64;
      if (P[19] = 3) and (Str(At(P, 0)) = Name) then
      begin
        Result := P;
        Exit;
      end;
    end;
    TI := At(TI, 24);
  end;
  Result := nil;
end;

var
  C: TChild;
  TI, P: PByte;
  I: Integer;
begin
  C := TChild.Create;
  C.FName := 'obj';
  C.FCount := 5;
  C.FColor := Green;
  TI := At(PPointer(C)^, 24);
  Writeln('typeinfo ', Pointer(TI) = TypeInfo(TChild), ' vmt ', At(TI, 16) = PPointer(C)^);
  while TI <> nil do
  begin
    Writeln('class ', Str(At(TI, 8)), ' kind ', TI[0], ' unit ', Str(At(TI, 32)), ' parent ', ParentName(TI));
    for I := 0 to PNativeInt(TI + 40)^ - 1 do
    begin
      P := PByte(At(TI, 48)) + I * 64;
      if P[19] = 3 then
        ShowProp(C, P);
    end;
    TI := At(TI, 24);
  end;

  P := FindProp(C, 'Count');
  TSetter(Pointer(PNativeInt(P + 32)^))(Pointer(C), 7);
  P := FindProp(C, 'Name');
  PStr(PByte(C) + PNativeInt(P + 32)^)^ := 'written';
  P := FindProp(C, 'Color');
  (PByte(C) + PNativeInt(P + 32)^)^ := Ord(Blue);
  Writeln('hidden published ', FindProp(C, 'Hidden') <> nil);
  Writeln(C.Count, ' ', C.Name, ' ', Ord(C.Color));
  C.Free;
end.
