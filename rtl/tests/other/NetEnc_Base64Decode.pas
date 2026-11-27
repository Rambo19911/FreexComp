// phase: 8
// covers: RTL-1208, RTL-1209, RTL-1210
program NetEnc_Base64Decode;
{ NetEncoding, Base64 decoding (rtl/phase8/encodings-and-hashes.md section 3.3, vectors 6.2 C): every row once with
  a tolerant and once with a strict object of the standard and of the URL-safe class, through the string form and the
  byte-array form. Tolerant decoding skips what it does not know, stops at the first pad character and never
  raises; strict decoding skips white space only and raises EConvertError for a foreign character (with its
  1-based position), a wrong length, wrong or missing padding and pad bits that are not 0. }
uses
  SysUtils, Classes, RTLConsts, NetEncoding, TestKit;

const
  CharAt = 'EConvertError: Invalid Base64 character at position ';
  BadLength = 'EConvertError: Invalid Base64 data length';
  BadPadding = 'EConvertError: Invalid Base64 padding';

var
  TolStd, TolUrl, StrictStd, StrictUrl: TCustomBase64Encoding;

function ToBytes(const S: string): TBytes;
var
  I: Integer;
begin
  SetLength(Result, Length(S));
  for I := 1 to Length(S) do
    Result[I - 1] := Byte(Ord(S[I]));
end;

function ToText(const A: TBytes): string;
var
  I: Integer;
begin
  SetLength(Result, Length(A));
  for I := 0 to Length(A) - 1 do
    Result[I + 1] := Char(A[I]);
end;

{ what decoding Input gives: '=' and the bytes (one character per byte), or the class and message of the exception;
  Bytes selects the byte-array form (Input has one character per byte), otherwise the string form is used }
function Outcome(Enc: TNetEncoding; const Input: string; Bytes: Boolean): string;
begin
  try
    if Bytes then
      Result := '=' + ToText(Enc.Decode(ToBytes(Input)))
    else
      Result := '=' + ToText(Enc.DecodeStringToBytes(Input));
  except
    on E: Exception do
      Result := E.ClassName + ': ' + E.Message;
  end;
end;

procedure One(Enc: TNetEncoding; const Name, Input, Expected: string);
begin
  CheckEqS(Expected, Outcome(Enc, Input, False), Name + ', string');
  CheckEqS(Expected, Outcome(Enc, Input, True), Name + ', bytes');
end;

{ T: both tolerant objects; SStd, SUrl: the strict object of each class }
procedure Row(const Name, Input, T, SStd, SUrl: string);
begin
  One(TolStd, Name + ': tolerant standard', Input, T);
  One(TolUrl, Name + ': tolerant URL', Input, T);
  One(StrictStd, Name + ': strict standard', Input, SStd);
  One(StrictUrl, Name + ': strict URL', Input, SUrl);
end;

procedure Run;
begin
  TolStd := TBase64StringEncoding.Create;
  TolUrl := TBase64URLEncoding.Create;
  StrictStd := TBase64StringEncoding.Create(0, '', bdStrict);
  StrictUrl := TBase64URLEncoding.Create(0, '', bdStrict);

  Section('the messages (RTL-1210)');
  CheckEqS('Invalid Base64 character at position %d', SBase64InvalidChar, 'SBase64InvalidChar');
  CheckEqS('Invalid Base64 data length', SBase64InvalidLength, 'SBase64InvalidLength');
  CheckEqS('Invalid Base64 padding', SBase64InvalidPadding, 'SBase64InvalidPadding');

  Section('white space is skipped in both modes (RTL-1208, RTL-1209)');
  Row('Zm9vYmFy', 'Zm9vYmFy', '=foobar', '=foobar', '=foobar');
  Row('a space inside', 'Zm9v YmFy', '=foobar', '=foobar', '=foobar');
  Row('CR LF inside', 'Zm9v'#13#10'YmFy', '=foobar', '=foobar', '=foobar');
  Row('spaces around the pad characters', ' Zg = = ', '=f', '=f', '=f');
  Row('tab and line feed', 'Zm'#9'9v'#10, '=foo', '=foo', '=foo');
  Row('white space only', ' '#9#13#10, '=', '=', '=');
  Row('empty', '', '=', '=', '=');

  Section('a foreign character (RTL-1208 (1), RTL-1209 (1))');
  Row('Zm9v*YmFy', 'Zm9v*YmFy', '=foobar', CharAt + '5', CharAt + '5');
  Row('the position counts white space', 'Zm9v'#13#10'*', '=foo', CharAt + '7', CharAt + '7');
  Row('a vertical tab is not white space', 'Zm9v'#11, '=foo', CharAt + '5', CharAt + '5');

  Section('padding and length (RTL-1208 (2) (3), RTL-1209 (2) (3))');
  Row('Zg', 'Zg', '=f', BadPadding, '=f');
  Row('Zg=', 'Zg=', '=f', BadPadding, BadPadding);
  Row('Zg==', 'Zg==', '=f', '=f', '=f');
  Row('Zg===', 'Zg===', '=f', BadPadding, BadPadding);
  Row('Zm8', 'Zm8', '=fo', BadPadding, '=fo');
  Row('Zm8=', 'Zm8=', '=fo', '=fo', '=fo');
  Row('Z', 'Z', '=', BadLength, BadLength);
  Row('Zm9vY', 'Zm9vY', '=foo', BadLength, BadLength);
  Row('Zg==Zg==', 'Zg==Zg==', '=f', BadPadding, BadPadding);
  Row('an alphabet character after a pad character', 'Zg=A', '=f', BadPadding, BadPadding);
  Row('a pad character alone', '=', '=', BadPadding, BadPadding);
  Row('a foreign character before the wrong length is reported first', 'Z*', '=', CharAt + '2', CharAt + '2');

  Section('pad bits (RTL-1208 (3), RTL-1209 (4))');
  Row('Zh==', 'Zh==', '=f', BadPadding, BadPadding);
  Row('Zm9=', 'Zm9=', '=fo', BadPadding, BadPadding);
  One(StrictUrl, 'Zh without padding: strict URL', 'Zh', BadPadding);
  One(TolUrl, 'Zh without padding: tolerant URL', 'Zh', '=f');

  Section('each class knows its own alphabet only');
  One(TolStd, '-_8: tolerant standard', '-_8', '=');
  One(StrictStd, '-_8: strict standard', '-_8', CharAt + '1');
  One(TolUrl, '-_8: tolerant URL', '-_8', '='#$FB#$FF);
  One(StrictUrl, '-_8: strict URL', '-_8', '='#$FB#$FF);
  One(TolUrl, '+/8=: tolerant URL', '+/8=', '=');
  One(StrictUrl, '+/8=: strict URL', '+/8=', CharAt + '1');
  One(TolStd, '+/8=: tolerant standard', '+/8=', '='#$FB#$FF);
  One(StrictStd, '+/8=: strict standard', '+/8=', '='#$FB#$FF);

  Section('bytes and characters outside ASCII');
  CheckEqS('=foo', Outcome(TolStd, 'Zm9v'#$FF, True), 'bytes 5A 6D 39 76 FF: tolerant');
  CheckEqS(CharAt + '5', Outcome(StrictStd, 'Zm9v'#$FF, True), 'bytes 5A 6D 39 76 FF: strict');
  CheckEqS('=foo', Outcome(TolStd, 'Zm9v'#$0141, False), 'U+0141 in a string: tolerant');
  CheckEqS(CharAt + '5', Outcome(StrictStd, 'Zm9v'#$0141, False), 'U+0141 in a string: strict (not taken for A)');

  Section('strict objects read what the encoders write; Decode(string) raises too');
  StrictStd.Free;
  StrictStd := TBase64Encoding.Create(4, #13#10, bdStrict);
  CheckEqS('Zm9v'#13#10'YmE=', StrictStd.Encode('fooba'), 'encoded in lines of 4');
  CheckEqS('fooba', StrictStd.Decode(StrictStd.Encode('fooba')), 'and decoded strictly');
  Expect('EConvertError', 'Invalid Base64 character at position 3');
  try StrictStd.Decode('Zm!v'); except on E: Exception do CheckRaises('Decode(string)', E.ClassName, E.Message); end;
  Expect('EConvertError', 'Invalid Base64 data length');
  try StrictStd.Decode('Zm9vY'); except on E: Exception do CheckRaises('Decode(string): length', E.ClassName, E.Message); end;
  CheckEqS('fo', TNetEncoding.Base64.Decode('Zm9='), 'the shared instances are tolerant');

  TolStd.Free;
  TolUrl.Free;
  StrictStd.Free;
  StrictUrl.Free;
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
