// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
{ Generics.Defaults - comparer and equality-comparer interfaces and their default implementations
  (docs: rtl/other-units.md section 8.1, RTL-671). The default comparers are chosen from TypeInfo(T) once, when
  Default is called; comparing and hashing are done by the non-generic routines of the implementation section. }
unit Generics.Defaults;

{$POINTERMATH ON}

interface

uses
  SysUtils, TypInfo;

type
  IComparer<T> = interface
    function Compare(const Left, Right: T): Integer;
  end;

  IEqualityComparer<T> = interface
    function Equals(const Left, Right: T): Boolean;
    function GetHashCode(const Value: T): Integer;
  end;

  TComparison<T> = reference to function(const Left, Right: T): Integer;
  TEqualityComparison<T> = reference to function(const Left, Right: T): Boolean;
  THasher<T> = reference to function(const Value: T): Integer;

  TComparer<T> = class(TInterfacedObject, IComparer<T>)
  public
    class function Default: IComparer<T>;
    class function Construct(const Comparison: TComparison<T>): IComparer<T>;
    function Compare(const Left, Right: T): Integer; virtual; abstract;
  end;

  TEqualityComparer<T> = class(TInterfacedObject, IEqualityComparer<T>)
  public
    class function Default: IEqualityComparer<T>;
    class function Construct(const EqualityComparison: TEqualityComparison<T>;
      const Hasher: THasher<T>): IEqualityComparer<T>;
    function Equals(const Left, Right: T): Boolean; reintroduce; overload; virtual; abstract;
    function GetHashCode(const Value: T): Integer; reintroduce; overload; virtual; abstract;
  end;

implementation

uses
  RTLConsts;

type
  TDefaultComparer<T> = class(TComparer<T>)
  public
    function Compare(const Left, Right: T): Integer; override;
  end;

  TDelegatedComparer<T> = class(TComparer<T>)
  private
    FCompare: TComparison<T>;
  public
    constructor Create(const ACompare: TComparison<T>);
    function Compare(const Left, Right: T): Integer; override;
  end;

  TDefaultEqualityComparer<T> = class(TEqualityComparer<T>)
  public
    function Equals(const Left, Right: T): Boolean; override;
    function GetHashCode(const Value: T): Integer; override;
  end;

  TDelegatedEqualityComparer<T> = class(TEqualityComparer<T>)
  private
    FEquals: TEqualityComparison<T>;
    FHasher: THasher<T>;
  public
    constructor Create(const AEquals: TEqualityComparison<T>; const AHasher: THasher<T>);
    function Equals(const Left, Right: T): Boolean; override;
    function GetHashCode(const Value: T): Integer; override;
  end;

  { rtl-abi.md section 10: the data at offset 16 of tkRecord, tkArray and tkDynArray blocks }
  PRecordData = ^TRecordData;
  TRecordData = record
    Size: Int64;
    ManagedCount: Int64;
    ManagedFields: Pointer;
  end;

  PArrayData = ^TArrayData;
  TArrayData = record
    Size: Int64;
    ElCount: Int64;
    ElType: PTypeInfo;
  end;

  PDynArrayData = ^TDynArrayData;
  TDynArrayData = record
    ElSize: Int64;
    ElType: PTypeInfo;
    ManagedElType: PTypeInfo;
  end;

  PMethod = ^TMethod;
  PInt8 = ^ShortInt;
  PInt16 = ^SmallInt;

const
  FnvOffset = Cardinal($811C9DC5);
  FnvPrime = Cardinal($01000193);

{ ------------------------------------------------------------------ RTL-671: kinds, compare, hash by TypeInfo }

function KindData(TI: PTypeInfo): Pointer;
begin
  Result := PByte(TI) + 16;
end;

{ a value of this type holds a reference-counted or otherwise finalised part }
function IsManaged(TI: PTypeInfo): Boolean;
begin
  Result := False;
  if TI = nil then
    Exit;
  case TI^.Kind of
    tkUString, tkAString, tkDynArray, tkInterface:
      Result := True;
    tkRecord:
      Result := PRecordData(KindData(TI))^.ManagedCount > 0;
    tkArray:
      Result := IsManaged(PArrayData(KindData(TI))^.ElType);
  end;
end;

function Supported(TI: PTypeInfo): Boolean;
begin
  Result := False;
  if TI = nil then
    Exit;
  case TI^.Kind of
    tkInteger, tkChar, tkEnumeration, tkWChar, tkInt64, tkFloat, tkSet, tkUString, tkAString, tkSString,
    tkClass, tkMethod, tkInterface, tkPointer, tkClassRef, tkProcedure, tkDynArray:
      Result := True;
    tkRecord, tkArray:
      Result := not IsManaged(TI);
  end;
end;

procedure CheckSupported(TI: PTypeInfo);
var
  Name: string;
begin
  if not Supported(TI) then
  begin
    Name := '';
    if TI <> nil then
      Name := TI^.Name;
    raise ENotSupportedException.CreateFmt(SNoDefaultComparer, [Name]);
  end;
end;

function Sign64(L, R: Int64): Integer;
begin
  if L < R then
    Result := -1
  else if L > R then
    Result := 1
  else
    Result := 0;
end;

function SignU64(L, R: UInt64): Integer;
begin
  if L < R then
    Result := -1
  else if L > R then
    Result := 1
  else
    Result := 0;
end;

{ NaN sorts first and equals NaN; -0 = 0 }
function SignFloat(L, R: Double): Integer;
begin
  if L <> L then
  begin
    if R <> R then
      Result := 0
    else
      Result := -1;
  end
  else if R <> R then
    Result := 1
  else if L < R then
    Result := -1
  else if L > R then
    Result := 1
  else
    Result := 0;
end;

{ unsigned byte-wise order, then the shorter block first }
function CompareBytes(L, R: PByte; LCount, RCount: NativeInt): Integer;
var
  I, N: NativeInt;
begin
  N := LCount;
  if RCount < N then
    N := RCount;
  for I := 0 to N - 1 do
    if (L + I)^ <> (R + I)^ then
    begin
      if (L + I)^ < (R + I)^ then
        Result := -1
      else
        Result := 1;
      Exit;
    end;
  Result := Sign64(LCount, RCount);
end;

function SignedOrd(TI: PTypeInfo; P: Pointer): Int64;
begin
  case GetTypeData(TI)^.OrdType of
    otSByte: Result := PInt8(P)^;
    otUByte: Result := PByte(P)^;
    otSWord: Result := PInt16(P)^;
    otUWord: Result := PWord(P)^;
    otSLong: Result := PInteger(P)^;
    otULong: Result := PCardinal(P)^;
  else
    Result := PInt64(P)^;
  end;
end;

function DynLength(P: Pointer): NativeInt;
begin
  if PPointer(P)^ = nil then
    Result := 0
  else
    Result := PNativeInt(PByte(PPointer(P)^) - SizeOf(NativeInt))^;
end;

function CompareValues(TI: PTypeInfo; Size: Integer; L, R: Pointer): Integer;
var
  ElSize: NativeInt;
begin
  case TI^.Kind of
    tkInteger, tkChar, tkEnumeration, tkWChar:
      Result := Sign64(SignedOrd(TI, L), SignedOrd(TI, R));
    tkInt64:
      if GetTypeData(TI)^.OrdType = otUInt64 then
        Result := SignU64(PUInt64(L)^, PUInt64(R)^)
      else
        Result := Sign64(PInt64(L)^, PInt64(R)^);
    tkFloat:
      case GetTypeData(TI)^.FloatType of
        ftSingle: Result := SignFloat(PSingle(L)^, PSingle(R)^);
        ftCurr: Result := Sign64(PInt64(L)^, PInt64(R)^);
      else
        Result := SignFloat(PDouble(L)^, PDouble(R)^);
      end;
    tkUString:
      begin
        Result := CompareStr(PString(L)^, PString(R)^);
        if Result < 0 then
          Result := -1
        else if Result > 0 then
          Result := 1;
      end;
    tkAString:
      Result := CompareBytes(PPointer(L)^, PPointer(R)^, Length(PAnsiString(L)^), Length(PAnsiString(R)^));
    tkSString:
      Result := CompareBytes(PByte(L) + 1, PByte(R) + 1, PByte(L)^, PByte(R)^);
    tkClass, tkInterface, tkPointer, tkClassRef, tkProcedure:
      Result := SignU64(NativeUInt(PPointer(L)^), NativeUInt(PPointer(R)^));
    tkMethod:
      begin
        Result := SignU64(NativeUInt(PMethod(L)^.Code), NativeUInt(PMethod(R)^.Code));
        if Result = 0 then
          Result := SignU64(NativeUInt(PMethod(L)^.Data), NativeUInt(PMethod(R)^.Data));
      end;
    tkDynArray:
      begin
        ElSize := PDynArrayData(KindData(TI))^.ElSize;
        Result := Sign64(DynLength(L), DynLength(R));
        if Result = 0 then
          Result := CompareBytes(PPointer(L)^, PPointer(R)^, DynLength(L) * ElSize, DynLength(R) * ElSize);
      end;
  else
    Result := CompareBytes(L, R, Size, Size);             { tkSet and unmanaged records / static arrays }
  end;
end;

function Fnv(Hash: Cardinal; P: PByte; Count: NativeInt): Cardinal;
var
  I: NativeInt;
begin
  Result := Hash;
  for I := 0 to Count - 1 do
    Result := (Result xor (P + I)^) * FnvPrime;
end;

function HashValue(TI: PTypeInfo; Size: Integer; P: Pointer): Integer;
var
  H: Cardinal;
  D: Double;
  S: Single;
begin
  case TI^.Kind of
    tkFloat:
      case GetTypeData(TI)^.FloatType of
        ftSingle:
          begin
            S := PSingle(P)^;
            if S = 0 then
              S := 0;                                    { -0 hashes like 0 }
            if S <> S then
              PCardinal(@S)^ := $7FC00000;               { every NaN hashes alike }
            H := Fnv(FnvOffset, @S, SizeOf(S));
          end;
        ftCurr:
          H := Fnv(FnvOffset, P, 8);
      else
        begin
          D := PDouble(P)^;
          if D = 0 then
            D := 0;
          if D <> D then
            PUInt64(@D)^ := UInt64($7FF8000000000000);
          H := Fnv(FnvOffset, @D, SizeOf(D));
        end;
      end;
    tkUString:
      H := Fnv(FnvOffset, PPointer(P)^, Length(PString(P)^) * 2);
    tkAString:
      H := Fnv(FnvOffset, PPointer(P)^, Length(PAnsiString(P)^));
    tkSString:
      H := Fnv(FnvOffset, PByte(P) + 1, PByte(P)^);
    tkDynArray:
      H := Fnv(FnvOffset, PPointer(P)^, DynLength(P) * PDynArrayData(KindData(TI))^.ElSize);
  else
    H := Fnv(FnvOffset, P, Size);
  end;
  Result := Integer(H);
end;

{ ---------------------------------------------------------------------------------------------- comparers }

class function TComparer<T>.Default: IComparer<T>;
begin
  CheckSupported(TypeInfo(T));
  Result := TDefaultComparer<T>.Create;
end;

class function TComparer<T>.Construct(const Comparison: TComparison<T>): IComparer<T>;
begin
  Result := TDelegatedComparer<T>.Create(Comparison);
end;

function TDefaultComparer<T>.Compare(const Left, Right: T): Integer;
begin
  Result := CompareValues(TypeInfo(T), SizeOf(T), @Left, @Right);
end;

constructor TDelegatedComparer<T>.Create(const ACompare: TComparison<T>);
begin
  inherited Create;
  FCompare := ACompare;
end;

function TDelegatedComparer<T>.Compare(const Left, Right: T): Integer;
begin
  Result := FCompare(Left, Right);
end;

class function TEqualityComparer<T>.Default: IEqualityComparer<T>;
begin
  CheckSupported(TypeInfo(T));
  Result := TDefaultEqualityComparer<T>.Create;
end;

class function TEqualityComparer<T>.Construct(const EqualityComparison: TEqualityComparison<T>;
  const Hasher: THasher<T>): IEqualityComparer<T>;
begin
  Result := TDelegatedEqualityComparer<T>.Create(EqualityComparison, Hasher);
end;

function TDefaultEqualityComparer<T>.Equals(const Left, Right: T): Boolean;
begin
  Result := CompareValues(TypeInfo(T), SizeOf(T), @Left, @Right) = 0;
end;

function TDefaultEqualityComparer<T>.GetHashCode(const Value: T): Integer;
begin
  Result := HashValue(TypeInfo(T), SizeOf(T), @Value);
end;

constructor TDelegatedEqualityComparer<T>.Create(const AEquals: TEqualityComparison<T>; const AHasher: THasher<T>);
begin
  inherited Create;
  FEquals := AEquals;
  FHasher := AHasher;
end;

function TDelegatedEqualityComparer<T>.Equals(const Left, Right: T): Boolean;
begin
  Result := FEquals(Left, Right);
end;

function TDelegatedEqualityComparer<T>.GetHashCode(const Value: T): Integer;
begin
  Result := FHasher(Value);
end;

end.
