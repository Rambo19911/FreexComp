// covers: LANG-990, LANG-993
program HelperBuiltinTargets;

{ One record helper per predeclared type, each with Kind returning the type's name; a variable of each type prints its
  own name. Aliases share the helper of their type: LongInt, LongWord, WideChar, Real, Extended, UnicodeString, PWideChar
  and a declared alias print the name of the type they name. }

type
  TMyInt = Integer;

  TShortIntHelper = record helper for ShortInt
    function Kind: string;
  end;

  TSmallIntHelper = record helper for SmallInt
    function Kind: string;
  end;

  TIntegerHelper = record helper for Integer
    function Kind: string;
  end;

  TInt64Helper = record helper for Int64
    function Kind: string;
  end;

  TByteHelper = record helper for Byte
    function Kind: string;
  end;

  TWordHelper = record helper for Word
    function Kind: string;
  end;

  TCardinalHelper = record helper for Cardinal
    function Kind: string;
  end;

  TUInt64Helper = record helper for UInt64
    function Kind: string;
  end;

  TNativeIntHelper = record helper for NativeInt
    function Kind: string;
  end;

  TNativeUIntHelper = record helper for NativeUInt
    function Kind: string;
  end;

  TBooleanHelper = record helper for Boolean
    function Kind: string;
  end;

  TByteBoolHelper = record helper for ByteBool
    function Kind: string;
  end;

  TWordBoolHelper = record helper for WordBool
    function Kind: string;
  end;

  TLongBoolHelper = record helper for LongBool
    function Kind: string;
  end;

  TCharHelper = record helper for Char
    function Kind: string;
  end;

  TAnsiCharHelper = record helper for AnsiChar
    function Kind: string;
  end;

  TSingleHelper = record helper for Single
    function Kind: string;
  end;

  TDoubleHelper = record helper for Double
    function Kind: string;
  end;

  TCurrencyHelper = record helper for Currency
    function Kind: string;
  end;

  TStringHelper = record helper for string
    function Kind: string;
  end;

  TAnsiStringHelper = record helper for AnsiString
    function Kind: string;
  end;

  TUTF8StringHelper = record helper for UTF8String
    function Kind: string;
  end;

  TRawByteStringHelper = record helper for RawByteString
    function Kind: string;
  end;

  TShortStringHelper = record helper for ShortString
    function Kind: string;
  end;

  TPointerHelper = record helper for Pointer
    function Kind: string;
  end;

  TPCharHelper = record helper for PChar
    function Kind: string;
  end;

function TShortIntHelper.Kind: string;
begin
  Result := 'ShortInt';
end;

function TSmallIntHelper.Kind: string;
begin
  Result := 'SmallInt';
end;

function TIntegerHelper.Kind: string;
begin
  Result := 'Integer';
end;

function TInt64Helper.Kind: string;
begin
  Result := 'Int64';
end;

function TByteHelper.Kind: string;
begin
  Result := 'Byte';
end;

function TWordHelper.Kind: string;
begin
  Result := 'Word';
end;

function TCardinalHelper.Kind: string;
begin
  Result := 'Cardinal';
end;

function TUInt64Helper.Kind: string;
begin
  Result := 'UInt64';
end;

function TNativeIntHelper.Kind: string;
begin
  Result := 'NativeInt';
end;

function TNativeUIntHelper.Kind: string;
begin
  Result := 'NativeUInt';
end;

function TBooleanHelper.Kind: string;
begin
  Result := 'Boolean';
end;

function TByteBoolHelper.Kind: string;
begin
  Result := 'ByteBool';
end;

function TWordBoolHelper.Kind: string;
begin
  Result := 'WordBool';
end;

function TLongBoolHelper.Kind: string;
begin
  Result := 'LongBool';
end;

function TCharHelper.Kind: string;
begin
  Result := 'Char';
end;

function TAnsiCharHelper.Kind: string;
begin
  Result := 'AnsiChar';
end;

function TSingleHelper.Kind: string;
begin
  Result := 'Single';
end;

function TDoubleHelper.Kind: string;
begin
  Result := 'Double';
end;

function TCurrencyHelper.Kind: string;
begin
  Result := 'Currency';
end;

function TStringHelper.Kind: string;
begin
  Result := 'string';
end;

function TAnsiStringHelper.Kind: string;
begin
  Result := 'AnsiString';
end;

function TUTF8StringHelper.Kind: string;
begin
  Result := 'UTF8String';
end;

function TRawByteStringHelper.Kind: string;
begin
  Result := 'RawByteString';
end;

function TShortStringHelper.Kind: string;
begin
  Result := 'ShortString';
end;

function TPointerHelper.Kind: string;
begin
  Result := 'Pointer';
end;

function TPCharHelper.Kind: string;
begin
  Result := 'PChar';
end;

var
  VShortInt: ShortInt;
  VSmallInt: SmallInt;
  VInteger: Integer;
  VInt64: Int64;
  VByte: Byte;
  VWord: Word;
  VCardinal: Cardinal;
  VUInt64: UInt64;
  VNativeInt: NativeInt;
  VNativeUInt: NativeUInt;
  VBoolean: Boolean;
  VByteBool: ByteBool;
  VWordBool: WordBool;
  VLongBool: LongBool;
  VChar: Char;
  VAnsiChar: AnsiChar;
  VSingle: Single;
  VDouble: Double;
  VCurrency: Currency;
  Vstring: string;
  VAnsiString: AnsiString;
  VUTF8String: UTF8String;
  VRawByteString: RawByteString;
  VShortString: ShortString;
  VPointer: Pointer;
  VPChar: PChar;
  ALongInt: LongInt;
  ALongWord: LongWord;
  AWideChar: WideChar;
  AReal: Real;
  AExtended: Extended;
  AUnicodeString: UnicodeString;
  APWideChar: PWideChar;
  ATMyInt: TMyInt;
begin
  Writeln('ShortInt: ', VShortInt.Kind);
  Writeln('SmallInt: ', VSmallInt.Kind);
  Writeln('Integer: ', VInteger.Kind);
  Writeln('Int64: ', VInt64.Kind);
  Writeln('Byte: ', VByte.Kind);
  Writeln('Word: ', VWord.Kind);
  Writeln('Cardinal: ', VCardinal.Kind);
  Writeln('UInt64: ', VUInt64.Kind);
  Writeln('NativeInt: ', VNativeInt.Kind);
  Writeln('NativeUInt: ', VNativeUInt.Kind);
  Writeln('Boolean: ', VBoolean.Kind);
  Writeln('ByteBool: ', VByteBool.Kind);
  Writeln('WordBool: ', VWordBool.Kind);
  Writeln('LongBool: ', VLongBool.Kind);
  Writeln('Char: ', VChar.Kind);
  Writeln('AnsiChar: ', VAnsiChar.Kind);
  Writeln('Single: ', VSingle.Kind);
  Writeln('Double: ', VDouble.Kind);
  Writeln('Currency: ', VCurrency.Kind);
  Writeln('string: ', Vstring.Kind);
  Writeln('AnsiString: ', VAnsiString.Kind);
  Writeln('UTF8String: ', VUTF8String.Kind);
  Writeln('RawByteString: ', VRawByteString.Kind);
  Writeln('ShortString: ', VShortString.Kind);
  Writeln('Pointer: ', VPointer.Kind);
  Writeln('PChar: ', VPChar.Kind);
  Writeln('LongInt: ', ALongInt.Kind);
  Writeln('LongWord: ', ALongWord.Kind);
  Writeln('WideChar: ', AWideChar.Kind);
  Writeln('Real: ', AReal.Kind);
  Writeln('Extended: ', AExtended.Kind);
  Writeln('UnicodeString: ', AUnicodeString.Kind);
  Writeln('PWideChar: ', APWideChar.Kind);
  Writeln('TMyInt: ', ATMyInt.Kind);
end.
