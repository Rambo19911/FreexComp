{ TypInfo - access to the published RTTI the compiler emits (docs: rtl/classes.md section 1, interfaces/rtl-abi.md
  section 10). This unit holds the only Pascal declarations of the RTTI layout; everything that is specified as
  "published" considers only entries with Visibility 3 (ABI-144), so results do not depend on the RTTI scope. }
unit TypInfo;

interface

uses
  SysUtils;

{$POINTERMATH ON}

type
  TOrdType = (otSByte, otUByte, otSWord, otUWord, otSLong, otULong, otSInt64, otUInt64);   { ABI section 10 OrdType }
  TFloatType = (ftSingle, ftDouble, ftCurr);                                                { ABI section 10 FloatType; ftCurr = 2 (C-218) }
  TMemberVisibility = (mvPrivate, mvProtected, mvPublic, mvPublished);
  TTypeKinds = set of TTypeKind;

  PTypeInfo = ^TTypeInfo;
  PPTypeInfo = ^PTypeInfo;
  { the common header (ABI section 10): Kind, 7 zero bytes, Name (a static UnicodeString payload, '' when anonymous) }
  TTypeInfo = record
    Kind: TTypeKind;
    Reserved: array[0..6] of Byte;
    Name: string;
  end;

  { the kind-specific data at offset 16 of a type info block }
  PTypeData = ^TTypeData;
  TTypeData = record
    case TTypeKind of
      tkInteger, tkChar, tkEnumeration, tkWChar, tkInt64: (
        OrdType: TOrdType;
        OrdReserved: array[0..6] of Byte;
        MinValue: Int64;
        MaxValue: Int64;
        BaseType: PTypeInfo;        { tkEnumeration only: self for a base enumeration }
        NameList: PPointer);        { tkEnumeration only: MaxValue - MinValue + 1 static UnicodeString payloads }
      tkFloat: (
        FloatType: TFloatType);
      tkSet: (
        SetSize: Integer;
        SetReserved: Integer;
        CompType: PTypeInfo);
      tkClass: (
        ClassType: TClass;
        ParentInfo: PTypeInfo;      { nil for TObject }
        UnitName: PWideChar;
        PropCount: NativeInt;       { properties declared in this class that are in RTTI scope }
        PropTable: Pointer;
        MethodCount: NativeInt;
        MethodTable: Pointer;
        FieldCount: NativeInt;
        FieldTable: Pointer);
      tkMethod, tkProcedure: (
        IsFunction: Boolean;
        ParamCount: Byte);
      tkUString, tkAString: (
        CodePage: Word);
      tkSString: (
        MaxLength: Byte);
      tkClassRef: (
        InstanceType: PTypeInfo);
      tkPointer: (
        RefType: PTypeInfo);
  end;

  { ABI section 10.1 TPropInfo (64 bytes) }
  PPropInfo = ^TPropInfo;
  TPropInfo = record
    Name: string;
    PropType: PTypeInfo;
    GetKind: Byte;          { 0 none, 1 field, 2 static method, 3 virtual method }
    SetKind: Byte;
    StoredKind: Byte;       { 0 constant, 1 field, 2 static method, 3 virtual method }
    Visibility: TMemberVisibility;
    Reserved1: array[0..3] of Byte;
    GetProc: NativeInt;     { field offset | code address | VMT offset }
    SetProc: NativeInt;
    StoredProc: NativeInt;  { constant 0/1 | field offset | code address | VMT offset }
    Index: Integer;         { $80000000 when absent }
    Reserved2: Integer;
    Default: Int64;         { $8000000000000000 when absent }
  end;

  { ABI section 10.1 TFieldInfo (32 bytes) }
  PFieldInfo = ^TFieldInfo;
  TFieldInfo = record
    Name: string;
    Offset: NativeInt;
    FieldType: PTypeInfo;
    Visibility: TMemberVisibility;
    Reserved: array[0..6] of Byte;
  end;

  { ABI section 10.1 TMethodInfo (24 bytes) }
  PMethodInfo = ^TMethodInfo;
  TMethodInfo = record
    Name: string;
    Code: Pointer;
    Visibility: TMemberVisibility;
    Kind: Byte;             { 0 procedure, 1 function, 2 constructor, 3 destructor, 4/5 class procedure/function,
                              6/7 static class procedure/function }
    Reserved: array[0..5] of Byte;
  end;

  PPropList = ^TPropList;
  TPropList = array[0..16379] of PPropInfo;

  EPropertyError = class(Exception);
  EPropertyConvertError = class(Exception);

const
  tkProperties = [tkInteger, tkChar, tkEnumeration, tkFloat, tkSet, tkClass, tkMethod, tkWChar,
    tkUString, tkAString, tkSString, tkInt64];
  tkAny = [Low(TTypeKind)..High(TTypeKind)];
  NoIndex = Integer($80000000);
  NoDefault = Int64($8000000000000000);

{ type information }
function GetTypeName(TI: PTypeInfo): string;
function GetTypeKind(TI: PTypeInfo): TTypeKind;
function GetTypeData(TI: PTypeInfo): PTypeData;
function GetEnumName(TI: PTypeInfo; Value: Integer): string;
function GetEnumValue(TI: PTypeInfo; const Name: string): Integer;
function GetEnumNameCount(TI: PTypeInfo): Integer;
function SetToString(TI: PTypeInfo; Value: Integer; Brackets: Boolean = False): string; overload;
function SetToString(PropInfo: PPropInfo; Value: Integer; Brackets: Boolean = False): string; overload;
function StringToSet(TI: PTypeInfo; const Value: string): Integer; overload;
function StringToSet(PropInfo: PPropInfo; const Value: string): Integer; overload;

{ raw tables of one class (RTL-405): every entry in RTTI scope, members of this class only }
function GetFieldTable(TI: PTypeInfo; out Count: Integer): PFieldInfo;
function GetMethodTable(TI: PTypeInfo; out Count: Integer): PMethodInfo;
function GetDeclaredPropTable(TI: PTypeInfo; out Count: Integer): PPropInfo;

{ published properties }
function GetPropInfo(TI: PTypeInfo; const PropName: string): PPropInfo; overload;
function GetPropInfo(TI: PTypeInfo; const PropName: string; Kinds: TTypeKinds): PPropInfo; overload;
function GetPropInfo(AClass: TClass; const PropName: string): PPropInfo; overload;
function GetPropInfo(AClass: TClass; const PropName: string; Kinds: TTypeKinds): PPropInfo; overload;
function GetPropInfo(Instance: TObject; const PropName: string): PPropInfo; overload;
function GetPropInfo(Instance: TObject; const PropName: string; Kinds: TTypeKinds): PPropInfo; overload;
function FindPropInfo(Instance: TObject; const PropName: string): PPropInfo; overload;
function FindPropInfo(AClass: TClass; const PropName: string): PPropInfo; overload;
function IsPublishedProp(Instance: TObject; const PropName: string): Boolean; overload;
function IsPublishedProp(AClass: TClass; const PropName: string): Boolean; overload;
function GetPropList(TI: PTypeInfo; out PropList: PPropList): Integer; overload;
function GetPropList(TI: PTypeInfo; Kinds: TTypeKinds; PropList: PPropList; SortList: Boolean = True): Integer; overload;
function GetPropList(Instance: TObject; out PropList: PPropList): Integer; overload;
function PropType(Instance: TObject; const PropName: string): TTypeKind; overload;
function PropType(AClass: TClass; const PropName: string): TTypeKind; overload;
function PropIsType(Instance: TObject; const PropName: string; TypeKind: TTypeKind): Boolean; overload;
function PropIsType(AClass: TClass; const PropName: string; TypeKind: TTypeKind): Boolean; overload;
function IsStoredProp(Instance: TObject; PropInfo: PPropInfo): Boolean; overload;
function IsStoredProp(Instance: TObject; const PropName: string): Boolean; overload;

{ typed accessors (RTL-403, RTL-404) }
function GetOrdProp(Instance: TObject; PropInfo: PPropInfo): Int64; overload;
function GetOrdProp(Instance: TObject; const PropName: string): Int64; overload;
procedure SetOrdProp(Instance: TObject; PropInfo: PPropInfo; Value: Int64); overload;
procedure SetOrdProp(Instance: TObject; const PropName: string; Value: Int64); overload;
function GetEnumProp(Instance: TObject; PropInfo: PPropInfo): string; overload;
function GetEnumProp(Instance: TObject; const PropName: string): string; overload;
procedure SetEnumProp(Instance: TObject; PropInfo: PPropInfo; const Value: string); overload;
procedure SetEnumProp(Instance: TObject; const PropName: string; const Value: string); overload;
function GetSetProp(Instance: TObject; PropInfo: PPropInfo; Brackets: Boolean = False): string; overload;
function GetSetProp(Instance: TObject; const PropName: string; Brackets: Boolean = False): string; overload;
procedure SetSetProp(Instance: TObject; PropInfo: PPropInfo; const Value: string); overload;
procedure SetSetProp(Instance: TObject; const PropName: string; const Value: string); overload;
function GetStrProp(Instance: TObject; PropInfo: PPropInfo): string; overload;
function GetStrProp(Instance: TObject; const PropName: string): string; overload;
procedure SetStrProp(Instance: TObject; PropInfo: PPropInfo; const Value: string); overload;
procedure SetStrProp(Instance: TObject; const PropName: string; const Value: string); overload;
function GetFloatProp(Instance: TObject; PropInfo: PPropInfo): Double; overload;
function GetFloatProp(Instance: TObject; const PropName: string): Double; overload;
procedure SetFloatProp(Instance: TObject; PropInfo: PPropInfo; Value: Double); overload;
procedure SetFloatProp(Instance: TObject; const PropName: string; Value: Double); overload;
function GetInt64Prop(Instance: TObject; PropInfo: PPropInfo): Int64; overload;
function GetInt64Prop(Instance: TObject; const PropName: string): Int64; overload;
procedure SetInt64Prop(Instance: TObject; PropInfo: PPropInfo; Value: Int64); overload;
procedure SetInt64Prop(Instance: TObject; const PropName: string; Value: Int64); overload;
function GetObjectProp(Instance: TObject; PropInfo: PPropInfo; MinClass: TClass = nil): TObject; overload;
function GetObjectProp(Instance: TObject; const PropName: string; MinClass: TClass = nil): TObject; overload;
procedure SetObjectProp(Instance: TObject; PropInfo: PPropInfo; Value: TObject); overload;
procedure SetObjectProp(Instance: TObject; const PropName: string; Value: TObject); overload;
function GetObjectPropClass(PropInfo: PPropInfo): TClass; overload;
function GetObjectPropClass(Instance: TObject; const PropName: string): TClass; overload;
function GetMethodProp(Instance: TObject; PropInfo: PPropInfo): TMethod; overload;
function GetMethodProp(Instance: TObject; const PropName: string): TMethod; overload;
procedure SetMethodProp(Instance: TObject; PropInfo: PPropInfo; const Value: TMethod); overload;
procedure SetMethodProp(Instance: TObject; const PropName: string; const Value: TMethod); overload;

implementation

uses
  RTLConsts;

const
  VisPublished = 3;
  AccNone = 0;
  AccField = 1;
  AccStatic = 2;
  AccVirtual = 3;

{ ---------------------------------------------------------------- type information }

function GetTypeName(TI: PTypeInfo): string;
begin
  Result := TI^.Name;
end;

function GetTypeKind(TI: PTypeInfo): TTypeKind;
begin
  Result := TI^.Kind;
end;

function GetTypeData(TI: PTypeInfo): PTypeData;
begin
  Result := PTypeData(PByte(TI) + 16);
end;

{ the base enumeration of an enumeration or enumeration subrange }
function EnumBase(TI: PTypeInfo): PTypeInfo;
begin
  Result := TI;
  if (TI^.Kind = tkEnumeration) and (GetTypeData(TI)^.BaseType <> nil) then
    Result := GetTypeData(TI)^.BaseType;
end;

{ the name of the element at Value: the base enumeration's name list; '' out of range }
function GetEnumName(TI: PTypeInfo; Value: Integer): string;
var
  Base: PTypeInfo;
  D: PTypeData;
begin
  Result := '';
  if TI = nil then
    Exit;
  if TI^.Kind <> tkEnumeration then
  begin
    if TI^.Kind in [tkInteger, tkInt64] then
      Result := IntToStr(Value);
    Exit;
  end;
  Base := EnumBase(TI);
  D := GetTypeData(Base);
  if (Value < D^.MinValue) or (Value > D^.MaxValue) or (D^.NameList = nil) then
    Exit;
  Result := PString(@D^.NameList[Value - D^.MinValue])^;
end;

{ case-insensitive (ASCII) search over the base enumeration's names, restricted to TI's range; -1 if unknown }
function GetEnumValue(TI: PTypeInfo; const Name: string): Integer;
var
  Base: PTypeInfo;
  D, Own: PTypeData;
  V: Int64;
begin
  Result := -1;
  if (TI = nil) or (TI^.Kind <> tkEnumeration) then
    Exit;
  Own := GetTypeData(TI);
  Base := EnumBase(TI);
  D := GetTypeData(Base);
  if D^.NameList = nil then
    Exit;
  for V := Own^.MinValue to Own^.MaxValue do
    if (D^.NameList[V - D^.MinValue] <> nil) and SameText(PString(@D^.NameList[V - D^.MinValue])^, Name) then
    begin
      Result := Integer(V);
      Exit;
    end;
end;

function GetEnumNameCount(TI: PTypeInfo): Integer;
begin
  Result := Integer(GetTypeData(TI)^.MaxValue) + 1;
end;

{ the element type of a set type: (Low, High) of its ordinals }
procedure SetRange(TI: PTypeInfo; out Comp: PTypeInfo; out Lo, Hi: Integer);
begin
  if TI^.Kind <> tkSet then
    raise EPropertyConvertError.Create(SInvalidPropertyValue);
  Comp := GetTypeData(TI)^.CompType;
  Lo := Integer(GetTypeData(Comp)^.MinValue);
  Hi := Integer(GetTypeData(Comp)^.MaxValue);
  if Lo < 0 then
    Lo := 0;
  if Hi > 31 then
    Hi := 31;
end;

function SetToString(TI: PTypeInfo; Value: Integer; Brackets: Boolean): string;
var
  Comp: PTypeInfo;
  Lo, Hi, I: Integer;
begin
  Result := '';
  SetRange(TI, Comp, Lo, Hi);
  for I := Lo to Hi do
    if (Value and (Integer(1) shl I)) <> 0 then
    begin
      if Result <> '' then
        Result := Result + ',';
      Result := Result + GetEnumName(Comp, I);
    end;
  if Brackets then
    Result := '[' + Result + ']';
end;

function SetToString(PropInfo: PPropInfo; Value: Integer; Brackets: Boolean): string;
begin
  Result := SetToString(PropInfo^.PropType, Value, Brackets);
end;

{ the ordinal of an element name of Comp: an enumeration name, or a number for an integer element type }
function ElementValue(Comp: PTypeInfo; const Name: string): Integer;
begin
  if Comp^.Kind = tkEnumeration then
    Result := GetEnumValue(Comp, Name)
  else
    Result := StrToIntDef(Name, -1);
end;

{ comma-separated element names, optionally in [ ]; blanks around names are ignored }
function StringToSet(TI: PTypeInfo; const Value: string): Integer;
var
  Comp: PTypeInfo;
  Lo, Hi, Start, I, V: Integer;
  S, Name: string;
begin
  Result := 0;
  SetRange(TI, Comp, Lo, Hi);
  S := Trim(Value);
  if (Length(S) >= 1) and (S[1] = '[') then
  begin
    if S[Length(S)] <> ']' then
      raise EPropertyConvertError.Create(SInvalidPropertyValue);
    S := Copy(S, 2, Length(S) - 2);
  end;
  if Trim(S) = '' then
    Exit;
  Start := 1;
  for I := 1 to Length(S) + 1 do
    if (I > Length(S)) or (S[I] = ',') then
    begin
      Name := Trim(Copy(S, Start, I - Start));
      V := ElementValue(Comp, Name);
      if (Name = '') or (V < Lo) or (V > Hi) then
        raise EPropertyConvertError.Create(SInvalidPropertyValue);
      Result := Result or (Integer(1) shl V);
      Start := I + 1;
    end;
end;

function StringToSet(PropInfo: PPropInfo; const Value: string): Integer;
begin
  Result := StringToSet(PropInfo^.PropType, Value);
end;

{ ---------------------------------------------------------------- raw tables (RTL-405) }

function GetFieldTable(TI: PTypeInfo; out Count: Integer): PFieldInfo;
begin
  Count := Integer(GetTypeData(TI)^.FieldCount);
  Result := GetTypeData(TI)^.FieldTable;
end;

function GetMethodTable(TI: PTypeInfo; out Count: Integer): PMethodInfo;
begin
  Count := Integer(GetTypeData(TI)^.MethodCount);
  Result := GetTypeData(TI)^.MethodTable;
end;

function GetDeclaredPropTable(TI: PTypeInfo; out Count: Integer): PPropInfo;
begin
  Count := Integer(GetTypeData(TI)^.PropCount);
  Result := GetTypeData(TI)^.PropTable;
end;

{ ---------------------------------------------------------------- published properties }

{ the published property named PropName of TI or its nearest ancestor that has one; nil if none }
function GetPropInfo(TI: PTypeInfo; const PropName: string): PPropInfo;
var
  D: PTypeData;
  P: PPropInfo;
  I: NativeInt;
begin
  while (TI <> nil) and (TI^.Kind = tkClass) do
  begin
    D := GetTypeData(TI);
    P := D^.PropTable;
    for I := 0 to D^.PropCount - 1 do
    begin
      if (P[I].Visibility = mvPublished) and SameText(P[I].Name, PropName) then
      begin
        Result := @P[I];
        Exit;
      end;
    end;
    TI := D^.ParentInfo;
  end;
  Result := nil;
end;

function GetPropInfo(TI: PTypeInfo; const PropName: string; Kinds: TTypeKinds): PPropInfo;
begin
  Result := GetPropInfo(TI, PropName);
  if (Result <> nil) and not (Result^.PropType^.Kind in Kinds) then
    Result := nil;
end;

function GetPropInfo(AClass: TClass; const PropName: string): PPropInfo;
begin
  Result := GetPropInfo(PTypeInfo(AClass.ClassInfo), PropName);
end;

function GetPropInfo(AClass: TClass; const PropName: string; Kinds: TTypeKinds): PPropInfo;
begin
  Result := GetPropInfo(PTypeInfo(AClass.ClassInfo), PropName, Kinds);
end;

function GetPropInfo(Instance: TObject; const PropName: string): PPropInfo;
begin
  Result := GetPropInfo(PTypeInfo(Instance.ClassInfo), PropName);
end;

function GetPropInfo(Instance: TObject; const PropName: string; Kinds: TTypeKinds): PPropInfo;
begin
  Result := GetPropInfo(PTypeInfo(Instance.ClassInfo), PropName, Kinds);
end;

function FindPropInfo(AClass: TClass; const PropName: string): PPropInfo;
begin
  Result := GetPropInfo(AClass, PropName);
  if Result = nil then
    raise EPropertyError.CreateFmt(SUnknownProperty, [PropName]);
end;

function FindPropInfo(Instance: TObject; const PropName: string): PPropInfo;
begin
  Result := FindPropInfo(Instance.ClassType, PropName);
end;

function IsPublishedProp(Instance: TObject; const PropName: string): Boolean;
begin
  Result := GetPropInfo(Instance, PropName) <> nil;
end;

function IsPublishedProp(AClass: TClass; const PropName: string): Boolean;
begin
  Result := GetPropInfo(AClass, PropName) <> nil;
end;

{ RTL-402: ancestor properties first, each class in declaration order; a redeclared property keeps the ancestor's
  position and points to the descendant's info. Fills List (when not nil) and returns the count. }
function CollectProps(TI: PTypeInfo; Kinds: TTypeKinds; List: PPropList): Integer;
var
  Chain: array[0..255] of PTypeInfo;
  Depth, C, J, K: Integer;
  I: NativeInt;
  D: PTypeData;
  P: PPropInfo;
  Found: Boolean;
  Scratch: PPropList;
begin
  Depth := 0;
  while (TI <> nil) and (TI^.Kind = tkClass) and (Depth <= High(Chain)) do
  begin
    Chain[Depth] := TI;
    Depth := Depth + 1;
    TI := GetTypeData(TI)^.ParentInfo;
  end;
  { all published entries, unfiltered, so redeclarations keep their place }
  C := 0;
  for J := 0 to Depth - 1 do
    C := C + Integer(GetTypeData(Chain[J])^.PropCount);
  Scratch := nil;
  if C > 0 then
    GetMem(Scratch, C * SizeOf(PPropInfo));
  try
    C := 0;
    for J := Depth - 1 downto 0 do
    begin
      D := GetTypeData(Chain[J]);
      P := D^.PropTable;
      for I := 0 to D^.PropCount - 1 do
        if P[I].Visibility = mvPublished then
        begin
          Found := False;
          for K := 0 to C - 1 do
            if SameText(Scratch^[K]^.Name, P[I].Name) then
            begin
              Scratch^[K] := @P[I];
              Found := True;
              Break;
            end;
          if not Found then
          begin
            Scratch^[C] := @P[I];
            C := C + 1;
          end;
        end;
    end;
    Result := 0;
    for K := 0 to C - 1 do
      if Scratch^[K]^.PropType^.Kind in Kinds then
      begin
        if List <> nil then
          List^[Result] := Scratch^[K];
        Result := Result + 1;
      end;
  finally
    if Scratch <> nil then
      FreeMem(Scratch);
  end;
end;

function GetPropList(TI: PTypeInfo; out PropList: PPropList): Integer;
begin
  PropList := nil;
  Result := CollectProps(TI, tkAny, nil);
  if Result > 0 then
  begin
    GetMem(PropList, Result * SizeOf(PPropInfo));
    CollectProps(TI, tkAny, PropList);
  end;
end;

function GetPropList(Instance: TObject; out PropList: PPropList): Integer;
begin
  Result := GetPropList(PTypeInfo(Instance.ClassInfo), PropList);
end;

{ the filtered list into a caller buffer (PropList nil: count only); SortList sorts by name, ASCII case-insensitive,
  stable }
function GetPropList(TI: PTypeInfo; Kinds: TTypeKinds; PropList: PPropList; SortList: Boolean): Integer;
var
  I, J: Integer;
  T: PPropInfo;
begin
  Result := CollectProps(TI, Kinds, PropList);
  if SortList and (PropList <> nil) then
    for I := 1 to Result - 1 do
    begin
      T := PropList^[I];
      J := I - 1;
      while (J >= 0) and (CompareText(PropList^[J]^.Name, T^.Name) > 0) do
      begin
        PropList^[J + 1] := PropList^[J];
        J := J - 1;
      end;
      PropList^[J + 1] := T;
    end;
end;

function PropType(AClass: TClass; const PropName: string): TTypeKind;
begin
  Result := FindPropInfo(AClass, PropName)^.PropType^.Kind;
end;

function PropType(Instance: TObject; const PropName: string): TTypeKind;
begin
  Result := PropType(Instance.ClassType, PropName);
end;

function PropIsType(AClass: TClass; const PropName: string; TypeKind: TTypeKind): Boolean;
begin
  Result := PropType(AClass, PropName) = TypeKind;
end;

function PropIsType(Instance: TObject; const PropName: string; TypeKind: TTypeKind): Boolean;
begin
  Result := PropType(Instance, PropName) = TypeKind;
end;

{ ---------------------------------------------------------------- accessor invocation (ABI-141, RTL-404) }
{ Accessors are called as methods of Instance through method pointer types, so the IR signatures of ABI-141 hold:
  getter T (ptr Self [, i32 Index]) or void (ptr Self, ptr Result [, i32 Index]); setter void (ptr Self [, i32 Index],
  value). A virtual accessor is looked up in the instance's own VMT. }

type
  TGetU8 = function: Byte of object;
  TGetU8I = function(Index: Integer): Byte of object;
  TGetU16 = function: Word of object;
  TGetU16I = function(Index: Integer): Word of object;
  TGetU32 = function: Cardinal of object;
  TGetU32I = function(Index: Integer): Cardinal of object;
  TGetU64 = function: Int64 of object;
  TGetU64I = function(Index: Integer): Int64 of object;
  TGetSingle = function: Single of object;
  TGetSingleI = function(Index: Integer): Single of object;
  TGetDouble = function: Double of object;
  TGetDoubleI = function(Index: Integer): Double of object;
  PCurr = ^Currency;
  TGetCurr = function: Currency of object;
  TGetCurrI = function(Index: Integer): Currency of object;
  TGetUStr = function: UnicodeString of object;
  TGetUStrI = function(Index: Integer): UnicodeString of object;
  TGetAStr = function: AnsiString of object;
  TGetAStrI = function(Index: Integer): AnsiString of object;
  TGetSStr = function: ShortString of object;
  TGetSStrI = function(Index: Integer): ShortString of object;
  TGetMeth = function: TMethod of object;
  TGetMethI = function(Index: Integer): TMethod of object;
  TGetBool = function: Boolean of object;

  TSetU8 = procedure(Value: Byte) of object;
  TSetU8I = procedure(Index: Integer; Value: Byte) of object;
  TSetU16 = procedure(Value: Word) of object;
  TSetU16I = procedure(Index: Integer; Value: Word) of object;
  TSetU32 = procedure(Value: Cardinal) of object;
  TSetU32I = procedure(Index: Integer; Value: Cardinal) of object;
  TSetU64 = procedure(Value: Int64) of object;
  TSetU64I = procedure(Index: Integer; Value: Int64) of object;
  TSetSingle = procedure(Value: Single) of object;
  TSetSingleI = procedure(Index: Integer; Value: Single) of object;
  TSetDouble = procedure(Value: Double) of object;
  TSetDoubleI = procedure(Index: Integer; Value: Double) of object;
  TSetCurr = procedure(Value: Currency) of object;
  TSetCurrI = procedure(Index: Integer; Value: Currency) of object;
  TSetUStr = procedure(const Value: UnicodeString) of object;
  TSetUStrI = procedure(Index: Integer; const Value: UnicodeString) of object;
  TSetAStr = procedure(const Value: AnsiString) of object;
  TSetAStrI = procedure(Index: Integer; const Value: AnsiString) of object;
  TSetPtr = procedure(Value: Pointer) of object;
  TSetPtrI = procedure(Index: Integer; Value: Pointer) of object;
  TSetMeth = procedure(const Value: TMethod) of object;
  TSetMethI = procedure(Index: Integer; const Value: TMethod) of object;

{ the method (Instance, code) of an accessor of kind AccStatic or AccVirtual }
function AccessorMethod(Instance: TObject; Kind: Byte; Proc: NativeInt): TMethod;
begin
  Result.Data := Pointer(Instance);
  if Kind = AccVirtual then
    Result.Code := PPointer(PByte(PPointer(Instance)^) + Proc)^
  else
    Result.Code := Pointer(Proc);
end;

function WrongType(PropInfo: PPropInfo): EPropertyError;
begin
  Result := EPropertyError.CreateFmt(SPropertyWrongType, [PropInfo^.Name]);
end;

procedure CheckReadable(PropInfo: PPropInfo);
begin
  if PropInfo^.GetKind = AccNone then
    raise EPropertyError.CreateFmt(SWriteOnlyProperty, [PropInfo^.Name]);
end;

procedure CheckWritable(PropInfo: PPropInfo);
begin
  if PropInfo^.SetKind = AccNone then
    raise EPropertyError.CreateFmt(SReadOnlyProperty, [PropInfo^.Name]);
end;

{ the storage size (1, 2, 4, 8) and signedness of an ordinal-like property type, or 0 when not ordinal }
function OrdSize(TI: PTypeInfo; out Signed: Boolean): Integer;
begin
  Signed := False;
  Result := 0;
  case TI^.Kind of
    tkInteger, tkChar, tkEnumeration, tkWChar, tkInt64:
      case GetTypeData(TI)^.OrdType of
        otSByte: begin Result := 1; Signed := True; end;
        otUByte: Result := 1;
        otSWord: begin Result := 2; Signed := True; end;
        otUWord: Result := 2;
        otSLong: begin Result := 4; Signed := True; end;
        otULong: Result := 4;
        otSInt64: begin Result := 8; Signed := True; end;
        otUInt64: Result := 8;
      end;
    tkSet:
      Result := GetTypeData(TI)^.SetSize;
    tkClass, tkClassRef, tkPointer:
      Result := SizeOf(Pointer);
  end;
  if not (Result in [1, 2, 4, 8]) then
    Result := 0;
end;

{ reads Size bytes as an unsigned value, then sign-extends when Signed }
function Extend(V: Int64; Size: Integer; Signed: Boolean): Int64;
begin
  Result := V;
  case Size of
    1: begin Result := V and $FF; if Signed and (Result >= $80) then Result := Result - $100; end;
    2: begin Result := V and $FFFF; if Signed and (Result >= $8000) then Result := Result - $10000; end;
    4: begin Result := V and $FFFFFFFF; if Signed and (Result >= $80000000) then Result := Result - $100000000; end;
  end;
end;

function GetRawOrd(Instance: TObject; PropInfo: PPropInfo; Size: Integer): Int64;
var
  M: TMethod;
  P: PByte;
  HasIndex: Boolean;
  Idx: Integer;
begin
  CheckReadable(PropInfo);
  Result := 0;
  if PropInfo^.GetKind = AccField then
  begin
    P := PByte(Pointer(Instance)) + PropInfo^.GetProc;
    case Size of
      1: Result := P^;
      2: Result := PWord(P)^;
      4: Result := PCardinal(P)^;
      8: Result := PInt64(P)^;
    end;
    Exit;
  end;
  M := AccessorMethod(Instance, PropInfo^.GetKind, PropInfo^.GetProc);
  Idx := PropInfo^.Index;
  HasIndex := Idx <> NoIndex;
  case Size of
    1: if HasIndex then Result := TGetU8I(M)(Idx) else Result := TGetU8(M)();
    2: if HasIndex then Result := TGetU16I(M)(Idx) else Result := TGetU16(M)();
    4: if HasIndex then Result := TGetU32I(M)(Idx) else Result := TGetU32(M)();
    8: if HasIndex then Result := TGetU64I(M)(Idx) else Result := TGetU64(M)();
  end;
end;

procedure SetRawOrd(Instance: TObject; PropInfo: PPropInfo; Size: Integer; Value: Int64);
var
  M: TMethod;
  P: PByte;
  HasIndex: Boolean;
  Idx: Integer;
begin
  CheckWritable(PropInfo);
  if PropInfo^.SetKind = AccField then
  begin
    P := PByte(Pointer(Instance)) + PropInfo^.SetProc;
    case Size of
      1: P^ := Byte(Value);
      2: PWord(P)^ := Word(Value);
      4: PCardinal(P)^ := Cardinal(Value);
      8: PInt64(P)^ := Value;
    end;
    Exit;
  end;
  M := AccessorMethod(Instance, PropInfo^.SetKind, PropInfo^.SetProc);
  Idx := PropInfo^.Index;
  HasIndex := Idx <> NoIndex;
  case Size of
    1: if HasIndex then TSetU8I(M)(Idx, Byte(Value)) else TSetU8(M)(Byte(Value));
    2: if HasIndex then TSetU16I(M)(Idx, Word(Value)) else TSetU16(M)(Word(Value));
    4: if HasIndex then TSetU32I(M)(Idx, Cardinal(Value)) else TSetU32(M)(Cardinal(Value));
    8: if HasIndex then TSetU64I(M)(Idx, Value) else TSetU64(M)(Value);
  end;
end;

{ ---------------------------------------------------------------- ordinal, enum, set, Int64, class }

function GetOrdProp(Instance: TObject; PropInfo: PPropInfo): Int64;
var
  Size: Integer;
  Signed: Boolean;
begin
  Size := OrdSize(PropInfo^.PropType, Signed);
  if (Size = 0) or (PropInfo^.PropType^.Kind in [tkClassRef, tkPointer]) then
    raise WrongType(PropInfo);
  Result := Extend(GetRawOrd(Instance, PropInfo, Size), Size, Signed);
end;

function GetOrdProp(Instance: TObject; const PropName: string): Int64;
begin
  Result := GetOrdProp(Instance, FindPropInfo(Instance, PropName));
end;

procedure SetOrdProp(Instance: TObject; PropInfo: PPropInfo; Value: Int64);
var
  Size: Integer;
  Signed: Boolean;
begin
  Size := OrdSize(PropInfo^.PropType, Signed);
  if (Size = 0) or (PropInfo^.PropType^.Kind in [tkClassRef, tkPointer]) then
    raise WrongType(PropInfo);
  SetRawOrd(Instance, PropInfo, Size, Value);
end;

procedure SetOrdProp(Instance: TObject; const PropName: string; Value: Int64);
begin
  SetOrdProp(Instance, FindPropInfo(Instance, PropName), Value);
end;

function GetEnumProp(Instance: TObject; PropInfo: PPropInfo): string;
begin
  if PropInfo^.PropType^.Kind <> tkEnumeration then
    raise WrongType(PropInfo);
  Result := GetEnumName(PropInfo^.PropType, Integer(GetOrdProp(Instance, PropInfo)));
end;

function GetEnumProp(Instance: TObject; const PropName: string): string;
begin
  Result := GetEnumProp(Instance, FindPropInfo(Instance, PropName));
end;

procedure SetEnumProp(Instance: TObject; PropInfo: PPropInfo; const Value: string);
var
  V: Integer;
begin
  if PropInfo^.PropType^.Kind <> tkEnumeration then
    raise WrongType(PropInfo);
  V := GetEnumValue(PropInfo^.PropType, Value);
  if V < 0 then
    raise EPropertyConvertError.Create(SInvalidPropertyValue);
  SetOrdProp(Instance, PropInfo, V);
end;

procedure SetEnumProp(Instance: TObject; const PropName: string; const Value: string);
begin
  SetEnumProp(Instance, FindPropInfo(Instance, PropName), Value);
end;

function GetSetProp(Instance: TObject; PropInfo: PPropInfo; Brackets: Boolean): string;
begin
  if PropInfo^.PropType^.Kind <> tkSet then
    raise WrongType(PropInfo);
  Result := SetToString(PropInfo^.PropType, Integer(GetOrdProp(Instance, PropInfo)), Brackets);
end;

function GetSetProp(Instance: TObject; const PropName: string; Brackets: Boolean): string;
begin
  Result := GetSetProp(Instance, FindPropInfo(Instance, PropName), Brackets);
end;

procedure SetSetProp(Instance: TObject; PropInfo: PPropInfo; const Value: string);
begin
  if PropInfo^.PropType^.Kind <> tkSet then
    raise WrongType(PropInfo);
  SetOrdProp(Instance, PropInfo, StringToSet(PropInfo^.PropType, Value));
end;

procedure SetSetProp(Instance: TObject; const PropName: string; const Value: string);
begin
  SetSetProp(Instance, FindPropInfo(Instance, PropName), Value);
end;

function GetInt64Prop(Instance: TObject; PropInfo: PPropInfo): Int64;
begin
  if not (PropInfo^.PropType^.Kind in [tkInt64, tkInteger, tkChar, tkWChar, tkEnumeration]) then
    raise WrongType(PropInfo);
  Result := GetOrdProp(Instance, PropInfo);
end;

function GetInt64Prop(Instance: TObject; const PropName: string): Int64;
begin
  Result := GetInt64Prop(Instance, FindPropInfo(Instance, PropName));
end;

procedure SetInt64Prop(Instance: TObject; PropInfo: PPropInfo; Value: Int64);
begin
  if not (PropInfo^.PropType^.Kind in [tkInt64, tkInteger, tkChar, tkWChar, tkEnumeration]) then
    raise WrongType(PropInfo);
  SetOrdProp(Instance, PropInfo, Value);
end;

procedure SetInt64Prop(Instance: TObject; const PropName: string; Value: Int64);
begin
  SetInt64Prop(Instance, FindPropInfo(Instance, PropName), Value);
end;

function GetObjectPropClass(PropInfo: PPropInfo): TClass;
begin
  if PropInfo^.PropType^.Kind <> tkClass then
    raise WrongType(PropInfo);
  Result := GetTypeData(PropInfo^.PropType)^.ClassType;
end;

function GetObjectPropClass(Instance: TObject; const PropName: string): TClass;
begin
  Result := GetObjectPropClass(FindPropInfo(Instance, PropName));
end;

{ nil when the value is not a MinClass (MinClass nil: any) }
function GetObjectProp(Instance: TObject; PropInfo: PPropInfo; MinClass: TClass): TObject;
begin
  if PropInfo^.PropType^.Kind <> tkClass then
    raise WrongType(PropInfo);
  Result := TObject(Pointer(NativeInt(GetRawOrd(Instance, PropInfo, SizeOf(Pointer)))));
  if (Result <> nil) and (MinClass <> nil) and not Result.InheritsFrom(MinClass) then
    Result := nil;
end;

function GetObjectProp(Instance: TObject; const PropName: string; MinClass: TClass): TObject;
begin
  Result := GetObjectProp(Instance, FindPropInfo(Instance, PropName), MinClass);
end;

procedure SetObjectProp(Instance: TObject; PropInfo: PPropInfo; Value: TObject);
begin
  if PropInfo^.PropType^.Kind <> tkClass then
    raise WrongType(PropInfo);
  SetRawOrd(Instance, PropInfo, SizeOf(Pointer), Int64(NativeInt(Pointer(Value))));
end;

procedure SetObjectProp(Instance: TObject; const PropName: string; Value: TObject);
begin
  SetObjectProp(Instance, FindPropInfo(Instance, PropName), Value);
end;

{ ---------------------------------------------------------------- strings }

{ a ShortString getter with a result of 2, 4 or 8 bytes returns it in a register (ABI-081) }
procedure GetShortStr(Instance: TObject; PropInfo: PPropInfo; Dest: PByte);
var
  M: TMethod;
  Size, Idx: Integer;
  V: Int64;
  Buf: ShortString;
begin
  Size := GetTypeData(PropInfo^.PropType)^.MaxLength + 1;
  if PropInfo^.GetKind = AccField then
  begin
    Move((PByte(Pointer(Instance)) + PropInfo^.GetProc)^, Dest^, Size);
    Exit;
  end;
  M := AccessorMethod(Instance, PropInfo^.GetKind, PropInfo^.GetProc);
  Idx := PropInfo^.Index;
  if Size in [2, 4, 8] then
  begin
    V := GetRawOrd(Instance, PropInfo, Size);
    Move(V, Dest^, Size);
  end
  else
  begin
    if Idx <> NoIndex then
      Buf := TGetSStrI(M)(Idx)
    else
      Buf := TGetSStr(M)();
    Move(Buf, Dest^, Size);
  end;
end;

function GetStrProp(Instance: TObject; PropInfo: PPropInfo): string;
var
  M: TMethod;
  Idx: Integer;
  A: AnsiString;
  S: ShortString;
begin
  CheckReadable(PropInfo);
  Idx := PropInfo^.Index;
  case PropInfo^.PropType^.Kind of
    tkUString:
      if PropInfo^.GetKind = AccField then
        Result := PUnicodeString(PByte(Pointer(Instance)) + PropInfo^.GetProc)^
      else
      begin
        M := AccessorMethod(Instance, PropInfo^.GetKind, PropInfo^.GetProc);
        if Idx <> NoIndex then
          Result := TGetUStrI(M)(Idx)
        else
          Result := TGetUStr(M)();
      end;
    tkAString:
      begin
        if PropInfo^.GetKind = AccField then
          A := PAnsiString(PByte(Pointer(Instance)) + PropInfo^.GetProc)^
        else
        begin
          M := AccessorMethod(Instance, PropInfo^.GetKind, PropInfo^.GetProc);
          if Idx <> NoIndex then
            A := TGetAStrI(M)(Idx)
          else
            A := TGetAStr(M)();
        end;
        Result := string(A);
      end;
    tkSString:
      begin
        S := '';
        GetShortStr(Instance, PropInfo, @S);
        Result := string(S);
      end;
  else
    raise WrongType(PropInfo);
  end;
end;

function GetStrProp(Instance: TObject; const PropName: string): string;
begin
  Result := GetStrProp(Instance, FindPropInfo(Instance, PropName));
end;

procedure SetStrProp(Instance: TObject; PropInfo: PPropInfo; const Value: string);
var
  M: TMethod;
  Idx, Size: Integer;
  A: AnsiString;
  S: ShortString;
  V: Int64;
begin
  CheckWritable(PropInfo);
  Idx := PropInfo^.Index;
  if PropInfo^.SetKind <> AccField then
    M := AccessorMethod(Instance, PropInfo^.SetKind, PropInfo^.SetProc);
  case PropInfo^.PropType^.Kind of
    tkUString:
      if PropInfo^.SetKind = AccField then
        PUnicodeString(PByte(Pointer(Instance)) + PropInfo^.SetProc)^ := Value
      else if Idx <> NoIndex then
        TSetUStrI(M)(Idx, Value)
      else
        TSetUStr(M)(Value);
    tkAString:
      begin
        A := '';
        _AStrFromUStr(PPointer(@A)^, Pointer(Value), GetTypeData(PropInfo^.PropType)^.CodePage);
        if PropInfo^.SetKind = AccField then
          PAnsiString(PByte(Pointer(Instance)) + PropInfo^.SetProc)^ := A
        else if Idx <> NoIndex then
          TSetAStrI(M)(Idx, A)
        else
          TSetAStr(M)(A);
      end;
    tkSString:
      begin
        Size := GetTypeData(PropInfo^.PropType)^.MaxLength + 1;
        _SStrFromUStr(@S, Size - 1, Pointer(Value));
        if PropInfo^.SetKind = AccField then
          Move(S, (PByte(Pointer(Instance)) + PropInfo^.SetProc)^, Size)
        else if Size in [2, 4, 8] then
        begin
          Move(S, V, Size);           { the bytes above Size are not passed: SetRawOrd narrows to Size }
          SetRawOrd(Instance, PropInfo, Size, V);
        end
        else if Idx <> NoIndex then
          TSetPtrI(M)(Idx, @S)
        else
          TSetPtr(M)(@S);
      end;
  else
    raise WrongType(PropInfo);
  end;
end;

procedure SetStrProp(Instance: TObject; const PropName: string; const Value: string);
begin
  SetStrProp(Instance, FindPropInfo(Instance, PropName), Value);
end;

{ ---------------------------------------------------------------- floats }

function GetFloatProp(Instance: TObject; PropInfo: PPropInfo): Double;
var
  M: TMethod;
  Idx: Integer;
  P: PByte;
  FT: TFloatType;
begin
  if PropInfo^.PropType^.Kind <> tkFloat then
    raise WrongType(PropInfo);
  CheckReadable(PropInfo);
  Idx := PropInfo^.Index;
  FT := GetTypeData(PropInfo^.PropType)^.FloatType;
  if PropInfo^.GetKind = AccField then
  begin
    P := PByte(Pointer(Instance)) + PropInfo^.GetProc;
    if FT = ftSingle then
      Result := PSingle(P)^
    else if FT = ftCurr then
      Result := PCurr(P)^
    else
      Result := PDouble(P)^;
    Exit;
  end;
  M := AccessorMethod(Instance, PropInfo^.GetKind, PropInfo^.GetProc);
  if FT = ftCurr then
  begin
    if Idx <> NoIndex then
      Result := TGetCurrI(M)(Idx)
    else
      Result := TGetCurr(M)();
  end
  else if FT = ftSingle then
  begin
    if Idx <> NoIndex then
      Result := TGetSingleI(M)(Idx)
    else
      Result := TGetSingle(M)();
  end
  else if Idx <> NoIndex then
    Result := TGetDoubleI(M)(Idx)
  else
    Result := TGetDouble(M)();
end;

function GetFloatProp(Instance: TObject; const PropName: string): Double;
begin
  Result := GetFloatProp(Instance, FindPropInfo(Instance, PropName));
end;

procedure SetFloatProp(Instance: TObject; PropInfo: PPropInfo; Value: Double);
var
  M: TMethod;
  Idx: Integer;
  P: PByte;
  S: Single;
  C: Currency;
  FT: TFloatType;
begin
  if PropInfo^.PropType^.Kind <> tkFloat then
    raise WrongType(PropInfo);
  CheckWritable(PropInfo);
  Idx := PropInfo^.Index;
  FT := GetTypeData(PropInfo^.PropType)^.FloatType;
  S := Value;
  C := Value;                                    { real -> Currency rounds to nearest (LANG-200, C-218) }
  if PropInfo^.SetKind = AccField then
  begin
    P := PByte(Pointer(Instance)) + PropInfo^.SetProc;
    if FT = ftSingle then
      PSingle(P)^ := S
    else if FT = ftCurr then
      PCurr(P)^ := C
    else
      PDouble(P)^ := Value;
    Exit;
  end;
  M := AccessorMethod(Instance, PropInfo^.SetKind, PropInfo^.SetProc);
  if FT = ftCurr then
  begin
    if Idx <> NoIndex then
      TSetCurrI(M)(Idx, C)
    else
      TSetCurr(M)(C);
  end
  else if FT = ftSingle then
  begin
    if Idx <> NoIndex then
      TSetSingleI(M)(Idx, S)
    else
      TSetSingle(M)(S);
  end
  else if Idx <> NoIndex then
    TSetDoubleI(M)(Idx, Value)
  else
    TSetDouble(M)(Value);
end;

procedure SetFloatProp(Instance: TObject; const PropName: string; Value: Double);
begin
  SetFloatProp(Instance, FindPropInfo(Instance, PropName), Value);
end;

{ ---------------------------------------------------------------- events }

function GetMethodProp(Instance: TObject; PropInfo: PPropInfo): TMethod;
var
  M: TMethod;
  Idx: Integer;
begin
  if PropInfo^.PropType^.Kind <> tkMethod then
    raise WrongType(PropInfo);
  CheckReadable(PropInfo);
  if PropInfo^.GetKind = AccField then
  begin
    Result := PMethod(PByte(Pointer(Instance)) + PropInfo^.GetProc)^;
    Exit;
  end;
  M := AccessorMethod(Instance, PropInfo^.GetKind, PropInfo^.GetProc);
  Idx := PropInfo^.Index;
  if Idx <> NoIndex then
    Result := TGetMethI(M)(Idx)
  else
    Result := TGetMeth(M)();
end;

function GetMethodProp(Instance: TObject; const PropName: string): TMethod;
begin
  Result := GetMethodProp(Instance, FindPropInfo(Instance, PropName));
end;

procedure SetMethodProp(Instance: TObject; PropInfo: PPropInfo; const Value: TMethod);
var
  M: TMethod;
  Idx: Integer;
begin
  if PropInfo^.PropType^.Kind <> tkMethod then
    raise WrongType(PropInfo);
  CheckWritable(PropInfo);
  if PropInfo^.SetKind = AccField then
  begin
    PMethod(PByte(Pointer(Instance)) + PropInfo^.SetProc)^ := Value;
    Exit;
  end;
  M := AccessorMethod(Instance, PropInfo^.SetKind, PropInfo^.SetProc);
  Idx := PropInfo^.Index;
  if Idx <> NoIndex then
    TSetMethI(M)(Idx, Value)
  else
    TSetMeth(M)(Value);
end;

procedure SetMethodProp(Instance: TObject; const PropName: string; const Value: TMethod);
begin
  SetMethodProp(Instance, FindPropInfo(Instance, PropName), Value);
end;

{ ---------------------------------------------------------------- stored }

{ the stored specifier: a constant, a Boolean field, or a parameterless Boolean function (LANG-644) }
function IsStoredProp(Instance: TObject; PropInfo: PPropInfo): Boolean;
var
  M: TMethod;
begin
  case PropInfo^.StoredKind of
    AccField:
      Result := PBoolean(PByte(Pointer(Instance)) + PropInfo^.StoredProc)^;
    AccStatic, AccVirtual:
      begin
        M := AccessorMethod(Instance, PropInfo^.StoredKind, PropInfo^.StoredProc);
        Result := TGetBool(M)();
      end;
  else
    Result := PropInfo^.StoredProc <> 0;
  end;
end;

function IsStoredProp(Instance: TObject; const PropName: string): Boolean;
begin
  Result := IsStoredProp(Instance, FindPropInfo(Instance, PropName));
end;

end.
