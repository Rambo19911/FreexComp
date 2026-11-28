// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
{ SysUtilsHelpers.inc - the bodies of the record helpers for built-in types that the interface of SysUtils declares
  (sysutils.md section 12, RTL-1260; the member list is rtl/helpers.md). Included once, at the end of the implementation
  part of SysUtils.pas. A member that the member list marks as a wrapper is one statement that calls its classic
  routine; every other member calls a classic routine or one of the shared routines below and holds no parsing or
  formatting code of its own (RTL-1262). No member changes Self (RTL-1263 (c)). }

{ ------------------------------------------------------------------------------------ shared routines (RTL-1262) }

{ S read with the grammar of StrToInt64 and within AMin..AMax: True and the value; otherwise False and 0 (RTL-1272) }
function HelperTryInt(const S: string; AMin, AMax: Int64; out Value: Int64): Boolean;
begin
  Result := TryStrToInt64(S, Value) and (Value >= AMin) and (Value <= AMax);
  if not Result then
    Value := 0;
end;

{ HelperTryInt, or EConvertError with SInvalidInteger and the text as given }
function HelperParseInt(const S: string; AMin, AMax: Int64): Int64;
begin
  if not HelperTryInt(S, AMin, AMax, Result) then
    ConvertError(SInvalidInteger, S);
end;

{ S read with the grammar of StrToUInt64 and at most AMax: True and the value; otherwise False and 0 }
function HelperTryUInt(const S: string; AMax: UInt64; out Value: UInt64): Boolean;
begin
  Result := TryStrToUInt64(S, Value) and (Value <= AMax);
  if not Result then
    Value := 0;
end;

{ HelperTryUInt, or EConvertError with SInvalidInteger and the text as given }
function HelperParseUInt(const S: string; AMax: UInt64): UInt64;
begin
  if not HelperTryUInt(S, AMax, Result) then
    ConvertError(SInvalidInteger, S);
end;

{ ------------------------------------------------------------------------------ integer helpers (RTL-1272) }

class function TShortIntHelper.Size: Integer;
begin
  Result := SizeOf(ShortInt);
end;

class function TShortIntHelper.ToString(Value: ShortInt): string;
begin
  Result := IntToStr(Value);
end;

class function TShortIntHelper.Parse(const S: string): ShortInt;
begin
  Result := ShortInt(HelperParseInt(S, Low(ShortInt), High(ShortInt)));
end;

class function TShortIntHelper.TryParse(const S: string; out Value: ShortInt): Boolean;
var
  V: Int64;
begin
  Result := HelperTryInt(S, Low(ShortInt), High(ShortInt), V);
  Value := ShortInt(V);
end;

function TShortIntHelper.ToString: string;
begin
  Result := IntToStr(Self);
end;

function TShortIntHelper.ToHexString: string;
begin
  Result := HexText(Byte(Self), 2 * SizeOf(ShortInt));
end;

function TShortIntHelper.ToHexString(MinDigits: Integer): string;
begin
  Result := HexText(Byte(Self), MinDigits);
end;

function TShortIntHelper.ToBoolean: Boolean;
begin
  Result := Self <> 0;
end;

function TShortIntHelper.ToSingle: Single;
begin
  Result := Self;
end;

function TShortIntHelper.ToDouble: Double;
begin
  Result := Self;
end;

function TShortIntHelper.ToExtended: Extended;
begin
  Result := Self;
end;

class function TSmallIntHelper.Size: Integer;
begin
  Result := SizeOf(SmallInt);
end;

class function TSmallIntHelper.ToString(Value: SmallInt): string;
begin
  Result := IntToStr(Value);
end;

class function TSmallIntHelper.Parse(const S: string): SmallInt;
begin
  Result := SmallInt(HelperParseInt(S, Low(SmallInt), High(SmallInt)));
end;

class function TSmallIntHelper.TryParse(const S: string; out Value: SmallInt): Boolean;
var
  V: Int64;
begin
  Result := HelperTryInt(S, Low(SmallInt), High(SmallInt), V);
  Value := SmallInt(V);
end;

function TSmallIntHelper.ToString: string;
begin
  Result := IntToStr(Self);
end;

function TSmallIntHelper.ToHexString: string;
begin
  Result := HexText(Word(Self), 2 * SizeOf(SmallInt));
end;

function TSmallIntHelper.ToHexString(MinDigits: Integer): string;
begin
  Result := HexText(Word(Self), MinDigits);
end;

function TSmallIntHelper.ToBoolean: Boolean;
begin
  Result := Self <> 0;
end;

function TSmallIntHelper.ToSingle: Single;
begin
  Result := Self;
end;

function TSmallIntHelper.ToDouble: Double;
begin
  Result := Self;
end;

function TSmallIntHelper.ToExtended: Extended;
begin
  Result := Self;
end;

class function TIntegerHelper.Size: Integer;
begin
  Result := SizeOf(Integer);
end;

class function TIntegerHelper.ToString(Value: Integer): string;
begin
  Result := IntToStr(Value);
end;

class function TIntegerHelper.Parse(const S: string): Integer;
begin
  Result := StrToInt(S);
end;

class function TIntegerHelper.TryParse(const S: string; out Value: Integer): Boolean;
var
  V: Int64;
begin
  Result := HelperTryInt(S, Low(Integer), High(Integer), V);
  Value := Integer(V);
end;

function TIntegerHelper.ToString: string;
begin
  Result := IntToStr(Self);
end;

function TIntegerHelper.ToHexString: string;
begin
  Result := HexText(Cardinal(Self), 2 * SizeOf(Integer));
end;

function TIntegerHelper.ToHexString(MinDigits: Integer): string;
begin
  Result := HexText(Cardinal(Self), MinDigits);
end;

function TIntegerHelper.ToBoolean: Boolean;
begin
  Result := Self <> 0;
end;

function TIntegerHelper.ToSingle: Single;
begin
  Result := Self;
end;

function TIntegerHelper.ToDouble: Double;
begin
  Result := Self;
end;

function TIntegerHelper.ToExtended: Extended;
begin
  Result := Self;
end;

class function TInt64Helper.Size: Integer;
begin
  Result := SizeOf(Int64);
end;

class function TInt64Helper.ToString(Value: Int64): string;
begin
  Result := IntToStr(Value);
end;

class function TInt64Helper.Parse(const S: string): Int64;
begin
  Result := StrToInt64(S);
end;

class function TInt64Helper.TryParse(const S: string; out Value: Int64): Boolean;
begin
  Result := HelperTryInt(S, Low(Int64), High(Int64), Value);
end;

function TInt64Helper.ToString: string;
begin
  Result := IntToStr(Self);
end;

function TInt64Helper.ToHexString: string;
begin
  Result := HexText(UInt64(Self), 2 * SizeOf(Int64));
end;

function TInt64Helper.ToHexString(MinDigits: Integer): string;
begin
  Result := HexText(UInt64(Self), MinDigits);
end;

function TInt64Helper.ToBoolean: Boolean;
begin
  Result := Self <> 0;
end;

function TInt64Helper.ToSingle: Single;
begin
  Result := Self;
end;

function TInt64Helper.ToDouble: Double;
begin
  Result := Self;
end;

function TInt64Helper.ToExtended: Extended;
begin
  Result := Self;
end;

class function TByteHelper.Size: Integer;
begin
  Result := SizeOf(Byte);
end;

class function TByteHelper.ToString(Value: Byte): string;
begin
  Result := IntToStr(Value);
end;

class function TByteHelper.Parse(const S: string): Byte;
begin
  Result := Byte(HelperParseInt(S, Low(Byte), High(Byte)));
end;

class function TByteHelper.TryParse(const S: string; out Value: Byte): Boolean;
var
  V: Int64;
begin
  Result := HelperTryInt(S, Low(Byte), High(Byte), V);
  Value := Byte(V);
end;

function TByteHelper.ToString: string;
begin
  Result := IntToStr(Self);
end;

function TByteHelper.ToHexString: string;
begin
  Result := HexText(Byte(Self), 2 * SizeOf(Byte));
end;

function TByteHelper.ToHexString(MinDigits: Integer): string;
begin
  Result := HexText(Byte(Self), MinDigits);
end;

function TByteHelper.ToBoolean: Boolean;
begin
  Result := Self <> 0;
end;

function TByteHelper.ToSingle: Single;
begin
  Result := Self;
end;

function TByteHelper.ToDouble: Double;
begin
  Result := Self;
end;

function TByteHelper.ToExtended: Extended;
begin
  Result := Self;
end;

class function TWordHelper.Size: Integer;
begin
  Result := SizeOf(Word);
end;

class function TWordHelper.ToString(Value: Word): string;
begin
  Result := IntToStr(Value);
end;

class function TWordHelper.Parse(const S: string): Word;
begin
  Result := Word(HelperParseInt(S, Low(Word), High(Word)));
end;

class function TWordHelper.TryParse(const S: string; out Value: Word): Boolean;
var
  V: Int64;
begin
  Result := HelperTryInt(S, Low(Word), High(Word), V);
  Value := Word(V);
end;

function TWordHelper.ToString: string;
begin
  Result := IntToStr(Self);
end;

function TWordHelper.ToHexString: string;
begin
  Result := HexText(Word(Self), 2 * SizeOf(Word));
end;

function TWordHelper.ToHexString(MinDigits: Integer): string;
begin
  Result := HexText(Word(Self), MinDigits);
end;

function TWordHelper.ToBoolean: Boolean;
begin
  Result := Self <> 0;
end;

function TWordHelper.ToSingle: Single;
begin
  Result := Self;
end;

function TWordHelper.ToDouble: Double;
begin
  Result := Self;
end;

function TWordHelper.ToExtended: Extended;
begin
  Result := Self;
end;

class function TCardinalHelper.Size: Integer;
begin
  Result := SizeOf(Cardinal);
end;

class function TCardinalHelper.ToString(Value: Cardinal): string;
begin
  Result := UIntToStr(Value);
end;

class function TCardinalHelper.Parse(const S: string): Cardinal;
begin
  Result := StrToUInt(S);
end;

class function TCardinalHelper.TryParse(const S: string; out Value: Cardinal): Boolean;
var
  V: UInt64;
begin
  Result := HelperTryUInt(S, High(Cardinal), V);
  Value := Cardinal(V);
end;

function TCardinalHelper.ToString: string;
begin
  Result := UIntToStr(Self);
end;

function TCardinalHelper.ToHexString: string;
begin
  Result := HexText(Cardinal(Self), 2 * SizeOf(Cardinal));
end;

function TCardinalHelper.ToHexString(MinDigits: Integer): string;
begin
  Result := HexText(Cardinal(Self), MinDigits);
end;

function TCardinalHelper.ToBoolean: Boolean;
begin
  Result := Self <> 0;
end;

function TCardinalHelper.ToSingle: Single;
begin
  Result := Self;
end;

function TCardinalHelper.ToDouble: Double;
begin
  Result := Self;
end;

function TCardinalHelper.ToExtended: Extended;
begin
  Result := Self;
end;

class function TUInt64Helper.Size: Integer;
begin
  Result := SizeOf(UInt64);
end;

class function TUInt64Helper.ToString(Value: UInt64): string;
begin
  Result := UIntToStr(Value);
end;

class function TUInt64Helper.Parse(const S: string): UInt64;
begin
  Result := StrToUInt64(S);
end;

class function TUInt64Helper.TryParse(const S: string; out Value: UInt64): Boolean;
begin
  Result := HelperTryUInt(S, High(UInt64), Value);
end;

function TUInt64Helper.ToString: string;
begin
  Result := UIntToStr(Self);
end;

function TUInt64Helper.ToHexString: string;
begin
  Result := HexText(UInt64(Self), 2 * SizeOf(UInt64));
end;

function TUInt64Helper.ToHexString(MinDigits: Integer): string;
begin
  Result := HexText(UInt64(Self), MinDigits);
end;

function TUInt64Helper.ToBoolean: Boolean;
begin
  Result := Self <> 0;
end;

function TUInt64Helper.ToSingle: Single;
begin
  Result := Self;
end;

function TUInt64Helper.ToDouble: Double;
begin
  Result := Self;
end;

function TUInt64Helper.ToExtended: Extended;
begin
  Result := Self;
end;

class function TNativeIntHelper.Size: Integer;
begin
  Result := SizeOf(NativeInt);
end;

class function TNativeIntHelper.ToString(Value: NativeInt): string;
begin
  Result := IntToStr(Value);
end;

class function TNativeIntHelper.Parse(const S: string): NativeInt;
begin
  Result := NativeInt(HelperParseInt(S, Low(NativeInt), High(NativeInt)));
end;

class function TNativeIntHelper.TryParse(const S: string; out Value: NativeInt): Boolean;
var
  V: Int64;
begin
  Result := HelperTryInt(S, Low(NativeInt), High(NativeInt), V);
  Value := NativeInt(V);
end;

function TNativeIntHelper.ToString: string;
begin
  Result := IntToStr(Self);
end;

function TNativeIntHelper.ToHexString: string;
begin
  Result := HexText(NativeUInt(Self), 2 * SizeOf(NativeInt));
end;

function TNativeIntHelper.ToHexString(MinDigits: Integer): string;
begin
  Result := HexText(NativeUInt(Self), MinDigits);
end;

function TNativeIntHelper.ToBoolean: Boolean;
begin
  Result := Self <> 0;
end;

function TNativeIntHelper.ToSingle: Single;
begin
  Result := Self;
end;

function TNativeIntHelper.ToDouble: Double;
begin
  Result := Self;
end;

function TNativeIntHelper.ToExtended: Extended;
begin
  Result := Self;
end;

class function TNativeUIntHelper.Size: Integer;
begin
  Result := SizeOf(NativeUInt);
end;

class function TNativeUIntHelper.ToString(Value: NativeUInt): string;
begin
  Result := UIntToStr(Value);
end;

class function TNativeUIntHelper.Parse(const S: string): NativeUInt;
begin
  Result := NativeUInt(HelperParseUInt(S, High(NativeUInt)));
end;

class function TNativeUIntHelper.TryParse(const S: string; out Value: NativeUInt): Boolean;
var
  V: UInt64;
begin
  Result := HelperTryUInt(S, High(NativeUInt), V);
  Value := NativeUInt(V);
end;

function TNativeUIntHelper.ToString: string;
begin
  Result := UIntToStr(Self);
end;

function TNativeUIntHelper.ToHexString: string;
begin
  Result := HexText(NativeUInt(Self), 2 * SizeOf(NativeUInt));
end;

function TNativeUIntHelper.ToHexString(MinDigits: Integer): string;
begin
  Result := HexText(NativeUInt(Self), MinDigits);
end;

function TNativeUIntHelper.ToBoolean: Boolean;
begin
  Result := Self <> 0;
end;

function TNativeUIntHelper.ToSingle: Single;
begin
  Result := Self;
end;

function TNativeUIntHelper.ToDouble: Double;
begin
  Result := Self;
end;

function TNativeUIntHelper.ToExtended: Extended;
begin
  Result := Self;
end;

{ ------------------------------------------------------------------------------ Boolean helpers (RTL-1274) }

class function TBooleanHelper.Size: Integer;
begin
  Result := SizeOf(Boolean);
end;

class function TBooleanHelper.ToString(Value: Boolean; UseBoolStrs: TUseBoolStrs): string;
begin
  Result := BoolToStr(Ord(Value) <> 0, UseBoolStrs = TUseBoolStrs.True);
end;

class function TBooleanHelper.Parse(const S: string): Boolean;
begin
  Result := StrToBool(S);
end;

class function TBooleanHelper.TryToParse(const S: string; out Value: Boolean): Boolean;
var
  B: Boolean;
begin
  Result := TryStrToBool(S, B);
  Value := Result and B;
end;

class function TBooleanHelper.TryParse(const S: string; out Value: Boolean): Boolean;
begin
  Result := TryToParse(S, Value);
end;

function TBooleanHelper.ToInteger: Integer;
begin
  Result := Ord(Ord(Self) <> 0);
end;

function TBooleanHelper.ToString(UseBoolStrs: TUseBoolStrs): string;
begin
  Result := BoolToStr(Ord(Self) <> 0, UseBoolStrs = TUseBoolStrs.True);
end;

class function TByteBoolHelper.Size: Integer;
begin
  Result := SizeOf(ByteBool);
end;

class function TByteBoolHelper.ToString(Value: ByteBool; UseBoolStrs: TUseBoolStrs): string;
begin
  Result := BoolToStr(Ord(Value) <> 0, UseBoolStrs = TUseBoolStrs.True);
end;

class function TByteBoolHelper.Parse(const S: string): ByteBool;
begin
  Result := StrToBool(S);
end;

class function TByteBoolHelper.TryToParse(const S: string; out Value: ByteBool): Boolean;
var
  B: Boolean;
begin
  Result := TryStrToBool(S, B);
  Value := Result and B;
end;

class function TByteBoolHelper.TryParse(const S: string; out Value: ByteBool): Boolean;
begin
  Result := TryToParse(S, Value);
end;

function TByteBoolHelper.ToInteger: Integer;
begin
  Result := Ord(Ord(Self) <> 0);
end;

function TByteBoolHelper.ToString(UseBoolStrs: TUseBoolStrs): string;
begin
  Result := BoolToStr(Ord(Self) <> 0, UseBoolStrs = TUseBoolStrs.True);
end;

class function TWordBoolHelper.Size: Integer;
begin
  Result := SizeOf(WordBool);
end;

class function TWordBoolHelper.ToString(Value: WordBool; UseBoolStrs: TUseBoolStrs): string;
begin
  Result := BoolToStr(Ord(Value) <> 0, UseBoolStrs = TUseBoolStrs.True);
end;

class function TWordBoolHelper.Parse(const S: string): WordBool;
begin
  Result := StrToBool(S);
end;

class function TWordBoolHelper.TryToParse(const S: string; out Value: WordBool): Boolean;
var
  B: Boolean;
begin
  Result := TryStrToBool(S, B);
  Value := Result and B;
end;

class function TWordBoolHelper.TryParse(const S: string; out Value: WordBool): Boolean;
begin
  Result := TryToParse(S, Value);
end;

function TWordBoolHelper.ToInteger: Integer;
begin
  Result := Ord(Ord(Self) <> 0);
end;

function TWordBoolHelper.ToString(UseBoolStrs: TUseBoolStrs): string;
begin
  Result := BoolToStr(Ord(Self) <> 0, UseBoolStrs = TUseBoolStrs.True);
end;

class function TLongBoolHelper.Size: Integer;
begin
  Result := SizeOf(LongBool);
end;

class function TLongBoolHelper.ToString(Value: LongBool; UseBoolStrs: TUseBoolStrs): string;
begin
  Result := BoolToStr(Ord(Value) <> 0, UseBoolStrs = TUseBoolStrs.True);
end;

class function TLongBoolHelper.Parse(const S: string): LongBool;
begin
  Result := StrToBool(S);
end;

class function TLongBoolHelper.TryToParse(const S: string; out Value: LongBool): Boolean;
var
  B: Boolean;
begin
  Result := TryStrToBool(S, B);
  Value := Result and B;
end;

class function TLongBoolHelper.TryParse(const S: string; out Value: LongBool): Boolean;
begin
  Result := TryToParse(S, Value);
end;

function TLongBoolHelper.ToInteger: Integer;
begin
  Result := Ord(Ord(Self) <> 0);
end;

function TLongBoolHelper.ToString(UseBoolStrs: TUseBoolStrs): string;
begin
  Result := BoolToStr(Ord(Self) <> 0, UseBoolStrs = TUseBoolStrs.True);
end;
