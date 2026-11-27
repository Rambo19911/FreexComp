// phase: 8
// covers: RTL-1200, RTL-1201, RTL-1202, RTL-1203, RTL-1205, RTL-1206, RTL-1207, RTL-1219
program NetEnc_Base64;
{ NetEncoding, the Base64 family (rtl/phase8/encodings-and-hashes.md section 3.1 to 3.3, vectors 6.2 A and B): the
  RFC 4648 section 10 vectors through every overload for the three shared instances; both alphabets; line length
  and separator (never a separator at the end, pad characters are counted); the shared instances are created once
  and have the documented classes; constructor defaults and the three properties; argument errors with class and
  message; a text kind written by a program overrides two methods and inherits every other form. }
uses
  SysUtils, Classes, NetEncoding, TestKit;

type
  { a text kind of the program's own: Encode wraps the text in angle brackets, Decode removes them (RTL-1201) }
  TAngleEncoding = class(TNetEncoding)
  protected
    function DoEncode(const Input: string): string; override;
    function DoDecode(const Input: string): string; override;
  end;

function TAngleEncoding.DoEncode(const Input: string): string;
begin
  Result := '<' + Input + '>';
end;

function TAngleEncoding.DoDecode(const Input: string): string;
begin
  Result := Copy(Input, 2, Length(Input) - 2);
end;

{ one byte per character (codes below 256) }
function ToBytes(const S: string): TBytes;
var
  I: Integer;
begin
  SetLength(Result, Length(S));
  for I := 1 to Length(S) do
    Result[I - 1] := Byte(Ord(S[I]));
end;

{ one character per byte }
function ToText(const A: TBytes): string;
var
  I: Integer;
begin
  SetLength(Result, Length(A));
  for I := 0 to Length(A) - 1 do
    Result[I + 1] := Char(A[I]);
end;

{ Raw (one character per byte) <-> Coded through every byte form of Enc. Text adds the two string forms, which go
  through UTF-8 and are therefore checked for ASCII rows only (RTL-1203). }
procedure Row(Enc: TNetEncoding; const Name, Raw, Coded: string; Text: Boolean);
var
  R, C: TBytes;
begin
  R := ToBytes(Raw);
  C := ToBytes(Coded);
  CheckEqS(Coded, Enc.EncodeBytesToString(R), Name + ': EncodeBytesToString(bytes)');
  CheckEqS(Coded, Enc.EncodeBytesToString(Pointer(R), Length(R)), Name + ': EncodeBytesToString(pointer, size)');
  CheckEqS(Coded, ToText(Enc.Encode(R)), Name + ': Encode(bytes)');
  CheckEqS(Raw, ToText(Enc.DecodeStringToBytes(Coded)), Name + ': DecodeStringToBytes');
  CheckEqS(Raw, ToText(Enc.Decode(C)), Name + ': Decode(bytes)');
  if Text then
  begin
    CheckEqS(Coded, Enc.Encode(Raw), Name + ': Encode(string)');
    CheckEqS(Raw, Enc.Decode(Coded), Name + ': Decode(string)');
  end;
end;

{ one row for an object the program creates and frees }
procedure OwnRow(Enc: TCustomBase64Encoding; const Name, Raw, Coded: string);
begin
  Row(Enc, Name, Raw, Coded, True);
  Enc.Free;
end;

procedure CheckProps(Enc: TCustomBase64Encoding; const Name: string; Chars: Integer; const Separator: string;
  Mode: TBase64DecodeMode; Owned: Boolean);
begin
  CheckEqI(Chars, Enc.CharsPerLine, Name + ': CharsPerLine');
  CheckEqS(Separator, Enc.LineSeparator, Name + ': LineSeparator');
  Check(Enc.DecodeMode = Mode, Name + ': DecodeMode');
  if Owned then
    Enc.Free;
end;

const
  Plain: array[0..6] of string = ('', 'f', 'fo', 'foo', 'foob', 'fooba', 'foobar');
  Std: array[0..6] of string = ('', 'Zg==', 'Zm8=', 'Zm9v', 'Zm9vYg==', 'Zm9vYmE=', 'Zm9vYmFy');
  Url: array[0..6] of string = ('', 'Zg', 'Zm8', 'Zm9v', 'Zm9vYg', 'Zm9vYmE', 'Zm9vYmFy');
  Line76 = 'AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8gISIjJCUmJygpKissLS4vMDEyMzQ1Njc4';
  CRLF = #13#10;

procedure Run;
var
  I: Integer;
  Raw: string;
  A: TAngleEncoding;
  P: TBytes;
  Src, Dst: TMemoryStream;
begin
  Section('RFC 4648 section 10 through every overload (RTL-1203, RTL-1205)');
  for I := 0 to 6 do
  begin
    Row(TNetEncoding.Base64, 'Base64 "' + Plain[I] + '"', Plain[I], Std[I], True);
    Row(TNetEncoding.Base64String, 'Base64String "' + Plain[I] + '"', Plain[I], Std[I], True);
    Row(TNetEncoding.Base64URL, 'Base64URL "' + Plain[I] + '"', Plain[I], Url[I], True);
  end;

  Section('the two alphabets and the padding (RTL-1205)');
  Row(TNetEncoding.Base64String, 'standard FB FF', #$FB#$FF, '+/8=', False);
  Row(TNetEncoding.Base64URL, 'URL FB FF', #$FB#$FF, '-_8', False);
  Row(TNetEncoding.Base64String, 'standard FB EF BE FF FF FF', #$FB#$EF#$BE#$FF#$FF#$FF, '++++////', False);
  Row(TNetEncoding.Base64URL, 'URL FB EF BE FF FF FF', #$FB#$EF#$BE#$FF#$FF#$FF, '----____', False);
  Row(TNetEncoding.Base64String, 'standard 00', #0, 'AA==', False);
  Row(TNetEncoding.Base64URL, 'URL 00', #0, 'AA', False);
  Row(TNetEncoding.Base64String, 'standard FF FF', #$FF#$FF, '//8=', False);
  Row(TNetEncoding.Base64URL, 'URL FF FF', #$FF#$FF, '__8', False);
  Row(TNetEncoding.Base64, 'Hello, World!', 'Hello, World!', 'SGVsbG8sIFdvcmxkIQ==', True);
  CheckEqS('w6k=', TNetEncoding.Base64String.Encode(#$00E9), 'Encode(string) encodes the UTF-8 form: U+00E9');
  CheckEqS(#$00E9, TNetEncoding.Base64String.Decode('w6k='), 'Decode(string) returns the UTF-8 text: U+00E9');
  CheckEqS('w6k=', TNetEncoding.Base64String.EncodeBytesToString([$C3, $A9]), 'the bytes C3 A9 as an array constructor');
  CheckEqS('77+9', TNetEncoding.Base64String.Encode(#$D83D), 'a lone surrogate is encoded as EF BF BD, not an error');

  Section('lines (RTL-1206)');
  Raw := '';
  for I := 0 to $38 do
    Raw := Raw + Char(I);
  Row(TNetEncoding.Base64, '57 bytes: one line of 76, no separator at the end', Raw, Line76, False);
  Row(TNetEncoding.Base64, '60 bytes: two lines', Raw + #$39#$3A#$3B, Line76 + CRLF + 'OTo7', False);
  Row(TNetEncoding.Base64String, '60 bytes without lines', Raw + #$39#$3A#$3B, Line76 + 'OTo7', False);
  OwnRow(TBase64Encoding.Create(4, #10), 'Create(4, LF)', 'foobar', 'Zm9v'#10'YmFy');
  OwnRow(TBase64Encoding.Create(5), 'Create(5): not a multiple of 4', 'foobar', 'Zm9vY' + CRLF + 'mFy');
  OwnRow(TBase64Encoding.Create(0), 'Create(0): no lines', 'foobar', 'Zm9vYmFy');
  OwnRow(TBase64URLEncoding.Create(4), 'TBase64URLEncoding.Create(4)', 'foobar', 'Zm9v' + CRLF + 'YmFy');
  OwnRow(TBase64Encoding.Create(2), 'Create(2): pad characters are counted', 'f', 'Zg' + CRLF + '==');
  OwnRow(TBase64Encoding.Create(4, ''), 'Create(4, empty separator)', 'foobar', 'Zm9vYmFy');
  OwnRow(TBase64StringEncoding.Create(3, ' '#9), 'TBase64StringEncoding.Create(3, space tab)', 'foo', 'Zm9 '#9'v');

  Section('shared instances (RTL-1202)');
  Check(TNetEncoding.Base64 = TNetEncoding.Base64, 'Base64 returns the same object twice');
  Check(TNetEncoding.Base64String = TNetEncoding.Base64String, 'Base64String returns the same object twice');
  Check(TNetEncoding.Base64URL = TNetEncoding.Base64URL, 'Base64URL returns the same object twice');
  CheckEqS('TBase64Encoding', TNetEncoding.Base64.ClassName, 'class of Base64');
  CheckEqS('TBase64StringEncoding', TNetEncoding.Base64String.ClassName, 'class of Base64String');
  CheckEqS('TBase64URLEncoding', TNetEncoding.Base64URL.ClassName, 'class of Base64URL');
  Check(TNetEncoding.Base64 is TCustomBase64Encoding, 'TBase64Encoding is a TCustomBase64Encoding');

  Section('constructors and properties (RTL-1207)');
  CheckProps(TNetEncoding.Base64 as TCustomBase64Encoding, 'shared Base64', 76, CRLF, bdTolerant, False);
  CheckProps(TNetEncoding.Base64String as TCustomBase64Encoding, 'shared Base64String', 0, '', bdTolerant, False);
  CheckProps(TNetEncoding.Base64URL as TCustomBase64Encoding, 'shared Base64URL', 0, '', bdTolerant, False);
  CheckProps(TBase64Encoding.Create, 'TBase64Encoding.Create', 76, CRLF, bdTolerant, True);
  CheckProps(TBase64StringEncoding.Create, 'TBase64StringEncoding.Create', 0, '', bdTolerant, True);
  CheckProps(TBase64URLEncoding.Create, 'TBase64URLEncoding.Create', 0, '', bdTolerant, True);
  CheckProps(TBase64StringEncoding.Create(10), 'TBase64StringEncoding.Create(10)', 10, CRLF, bdTolerant, True);
  CheckProps(TBase64URLEncoding.Create(64), 'TBase64URLEncoding.Create(64)', 64, CRLF, bdTolerant, True);
  CheckProps(TBase64Encoding.Create(10, #10), 'TBase64Encoding.Create(10, LF)', 10, #10, bdTolerant, True);
  CheckProps(TBase64Encoding.Create(8, ' ', bdStrict), 'TBase64Encoding.Create(8, space, bdStrict)', 8, ' ', bdStrict, True);
  Expect('EArgumentOutOfRangeException', 'Argument out of range');
  try TBase64Encoding.Create(-1).Free; except on E: Exception do CheckRaises('Create(-1)', E.ClassName, E.Message); end;
  try TBase64URLEncoding.Create(-5, '', bdStrict).Free; except on E: Exception do CheckRaises('Create(-5, empty, bdStrict)', E.ClassName, E.Message); end;
  Expect('EArgumentException', 'Invalid argument');
  try TBase64Encoding.Create(76, 'x').Free; except on E: Exception do CheckRaises('Create(76, x)', E.ClassName, E.Message); end;
  try TBase64StringEncoding.Create(0, #13'='#10).Free; except on E: Exception do CheckRaises('a separator with a pad character', E.ClassName, E.Message); end;

  Section('arguments (RTL-1219)');
  Expect('EArgumentOutOfRangeException', 'Argument out of range');
  try TNetEncoding.Base64.EncodeBytesToString(nil, -1); except on E: Exception do CheckRaises('EncodeBytesToString(nil, -1)', E.ClassName, E.Message); end;
  CheckEqS('', TNetEncoding.Base64.EncodeBytesToString(nil, 0), 'EncodeBytesToString(nil, 0) is empty');
  Expect('EArgumentNilException', 'Argument must not be nil');
  try TNetEncoding.Base64.EncodeBytesToString(nil, 1); except on E: Exception do CheckRaises('EncodeBytesToString(nil, 1)', E.ClassName, E.Message); end;

  Section('a text kind written by a program (RTL-1201, RTL-1203)');
  A := TAngleEncoding.Create;
  CheckEqS('<x>', A.Encode('x'), 'Encode(string) calls the override');
  CheckEqS('x', A.Decode('<x>'), 'Decode(string) calls the override');
  CheckEqS('<'#$00E9'>', A.EncodeBytesToString([$C3, $A9]), 'EncodeBytesToString reads the bytes as UTF-8 text');
  CheckEqS('<'#$C3#$A9'>', ToText(A.Encode([$C3, $A9])), 'Encode(bytes) returns the UTF-8 form of the result');
  CheckEqS(#$C3#$A9, ToText(A.Decode(ToBytes('<'#$C3#$A9'>'))), 'Decode(bytes)');
  CheckEqS(#$C3#$A9, ToText(A.DecodeStringToBytes('<'#$00E9'>')), 'DecodeStringToBytes returns the UTF-8 form');
  P := ToBytes('abc');
  CheckEqS('<ab>', A.EncodeBytesToString(Pointer(P), 2), 'EncodeBytesToString(pointer, size)');
  Expect('EArgumentNilException', 'Argument must not be nil');
  try A.EncodeBytesToString(nil, 1); except on E: Exception do CheckRaises('the argument rules hold for it too', E.ClassName, E.Message); end;
  Src := TMemoryStream.Create;
  Dst := TMemoryStream.Create;
  P := ToBytes('??x'#$C3#$A9);
  Src.WriteBuffer(P[0], Length(P));
  Src.Position := 2;
  CheckEqI(5, A.Encode(Src, Dst), 'Encode(streams) returns the bytes written');
  CheckEqI(5, Src.Position, 'the input is read to its end');
  SetLength(P, 5);
  Move(Dst.Memory^, P[0], 5);
  CheckEqS('<x'#$C3#$A9'>', ToText(P), 'the stream form writes the bytes of the array form');
  Dst.Position := 0;
  Src.Clear;
  CheckEqI(3, A.Decode(Dst, Src), 'Decode(streams)');
  SetLength(P, 3);
  Move(Src.Memory^, P[0], 3);
  CheckEqS('x'#$C3#$A9, ToText(P), 'and back');
  Src.Free;
  Dst.Free;
  A.Free;
end;

var
  Before: NativeInt;
begin
  { the shared instances and the UTF-8 encoding live until the program ends: they exist before the count is taken }
  TNetEncoding.Base64.Decode(TNetEncoding.Base64String.Encode(TNetEncoding.Base64URL.Encode('x')));
  Before := _LiveAllocCount;
  Run;
  CheckEqI(Before, _LiveAllocCount, 'no block is left');
  Done;
end.
