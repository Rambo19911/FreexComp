// phase: 8
// covers: RTL-1211, RTL-1212, RTL-1213, RTL-1214
program NetEnc_Url;
{ NetEncoding, URL percent-encoding (rtl/phase8/encodings-and-hashes.md section 3.4, vectors 6.2 E and F): the card
  vector through the four component encoders (path, query and form give three different results); every printable
  ASCII character through each of them; the general encoder with its set and options; UTF-8 first, then upper-case
  hex; round trips; decoding with hex digits of either case, "+" only as a form space, bytes that are not UTF-8,
  and EConvertError with the 1-based position of a malformed escape; the one-parameter and the byte and stream
  forms; the shared instance. }
uses
  SysUtils, Classes, NetEncoding, TestKit;

const
  EAcute = #$00E9;
  Grin = #$D83D#$DE00;               { U+1F600 as its surrogate pair }
  Card = 'a b&c=d/' + EAcute;
  CardPath = 'a%20b&c=d/%C3%A9';
  CardQuery = 'a%20b%26c%3Dd%2F%C3%A9';
  CardForm = 'a+b%26c%3Dd%2F%C3%A9';
  CardAuth = 'a%20b&c=d%2F%C3%A9';
  Upper = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ';
  Lower = 'abcdefghijklmnopqrstuvwxyz';
  { the 95 characters U+0020 to U+007E, in order, through each component encoder }
  AllPath = '%20!%22%23$%25&''()*+,-./0123456789:;%3C=%3E%3F@' + Upper + '%5B%5C%5D%5E_%60' + Lower + '%7B%7C%7D~';
  AllAuth = '%20!%22%23$%25&''()*+,-.%2F0123456789%3A;%3C=%3E%3F%40' + Upper + '%5B%5C%5D%5E_%60' + Lower + '%7B%7C%7D~';
  AllQuery = '%20%21%22%23%24%25%26%27%28%29%2A%2B%2C-.%2F0123456789%3A%3B%3C%3D%3E%3F%40' + Upper +
    '%5B%5C%5D%5E_%60' + Lower + '%7B%7C%7D~';
  AllForm = '+%21%22%23%24%25%26%27%28%29*%2B%2C-.%2F0123456789%3A%3B%3C%3D%3E%3F%40' + Upper +
    '%5B%5C%5D%5E_%60' + Lower + '%7B%7C%7D%7E';
  BadEscape = 'EConvertError: Invalid URL escape sequence at position ';

type
  TDecodeForm = (dfUrl, dfForm, dfOneParameter, dfBytes);

var
  U: TURLEncoding;

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

{ the content of a stream, one character per byte }
function StreamText(M: TMemoryStream): string;
var
  A: TBytes;
begin
  SetLength(A, Integer(M.Size));
  if Length(A) > 0 then
    Move(M.Memory^, A[0], Length(A));
  Result := ToText(A);
end;

function IsAscii(const S: string): Boolean;
var
  I: Integer;
begin
  Result := True;
  for I := 1 to Length(S) do
    if Ord(S[I]) > 127 then
      Result := False;
end;

{ '=' and the decoded text, or the class and message of the exception }
function Decoded(Form: TDecodeForm; const V: string): string;
begin
  try
    case Form of
      dfUrl: Result := '=' + U.URLDecode(V);
      dfForm: Result := '=' + U.FormDecode(V);
      dfOneParameter: Result := '=' + TNetEncoding.URL.Decode(V);
    else
      Result := '=' + ToText(U.Decode(ToBytes(V)));
    end;
  except
    on E: Exception do
      Result := E.ClassName + ': ' + E.Message;
  end;
end;

procedure Run;
var
  I: Integer;
  All, S: string;
  Own: TURLEncoding;
  P: TBytes;
  Src, Dst: TMemoryStream;
begin
  Section('the card vector: one text, each component rule (RTL-1212, RTL-1214)');
  CheckEqS(CardPath, U.EncodePath(Card), 'EncodePath keeps "&", "=" and "/"');
  CheckEqS(CardQuery, U.EncodeQuery(Card), 'EncodeQuery encodes every delimiter');
  CheckEqS(CardForm, U.EncodeForm(Card), 'EncodeForm writes "+" for the space');
  CheckEqS(CardAuth, U.EncodeAuth(Card), 'EncodeAuth keeps "&" and "=" but not "/"');
  CheckEqS(CardQuery, TNetEncoding.URL.Encode(Card), 'Encode with one parameter is EncodeQuery');
  Check((CardPath <> CardQuery) and (CardQuery <> CardForm) and (CardPath <> CardForm), 'path, query and form: three different results');

  Section('every printable ASCII character through each routine (RTL-1212)');
  All := '';
  for I := 32 to 126 do
    All := All + Char(I);
  CheckEqI(95, Length(All), '95 characters');
  CheckEqS(AllPath, U.EncodePath(All), 'EncodePath: unreserved, sub-delims, ":", "@", "/" are kept');
  CheckEqS(AllAuth, U.EncodeAuth(All), 'EncodeAuth: unreserved and sub-delims are kept');
  CheckEqS(AllQuery, U.EncodeQuery(All), 'EncodeQuery: only unreserved is kept');
  CheckEqS(AllForm, U.EncodeForm(All), 'EncodeForm: letters, digits, "*", "-", ".", "_" are kept; "~" is not');
  CheckEqS(AllQuery, U.Encode(All), 'Encode with one parameter');
  CheckEqS('a%2Fb', U.EncodePath('a/b', [Ord('/')]), 'EncodePath with "/" as an extra unsafe character');
  CheckEqS('a%2Db%2Ec', U.EncodeQuery('a-b.c', [Ord('-'), Ord('.')]), 'EncodeQuery with extra unsafe characters');
  CheckEqS('a%21%40b', U.EncodeAuth('a!@b', [Ord('!')]), 'EncodeAuth with an extra unsafe character');
  CheckEqS('a%2A+b', U.EncodeForm('a* b', [Ord('*')]), 'EncodeForm with an extra unsafe character');
  CheckEqS('', U.EncodePath('') + U.EncodeAuth('') + U.EncodeQuery('') + U.EncodeForm('') + U.Encode(''), 'empty text stays empty');

  Section('UTF-8 first, then percent-encoding with upper-case hex (RTL-1211)');
  CheckEqS('%09%7F%F0%9F%98%80', U.EncodeQuery(#9#127 + Grin), 'a tab, U+007F and U+1F600 (one 4-byte sequence)');
  CheckEqS('%EF%BF%BD', U.EncodeQuery(#$D83D), 'a lone high surrogate becomes EF BF BD');
  CheckEqS('%EF%BF%BD%EF%BF%BD', U.EncodePath(#$DE00#$D83D), 'a pair in the wrong order is two lone surrogates');
  CheckEqS('%E2%82%AC%00%1F', U.EncodeForm(#$20AC#0#31), 'U+20AC and control characters');
  CheckEqS('%3F', U.EncodeForm(EAcute, [], TEncoding.ASCII), 'another encoding supplies the bytes: ASCII');
  CheckEqS('%C3%A9', U.EncodeQuery(EAcute, []), 'an empty extra set written out');
  Check(IsAscii(U.EncodePath(All + EAcute + Grin + #$FFFF)), 'the result holds ASCII characters only');

  Section('the general encoder: set and options (RTL-1211)');
  CheckEqS('a b%c', U.Encode('a b%c', [], []), 'empty set, no option: printable ASCII is copied, "%" too');
  CheckEqS('a+b%25c%2B', U.Encode('a b%c+', [Ord('+')], [TURLEncoding.SpacesAsPlus, TURLEncoding.EncodePercent]),
    'SpacesAsPlus, EncodePercent and "+" in the set');
  CheckEqS('%C3%A9%01', U.Encode(EAcute + #1, [], []), 'non-ASCII and control characters are always encoded');
  CheckEqS('%25', U.Encode('%', [37], []), '"%" in the set is encoded without the option');
  CheckEqS('%25', U.Encode('%', [], [TURLEncoding.TEncodeOption.EncodePercent]), 'EncodePercent, spelled with the type name');
  CheckEqS('a%20b', U.Encode('a b', [32], []), 'a space in the set');
  CheckEqS('a+b', U.Encode('a b', [32], [TURLEncoding.SpacesAsPlus]), 'SpacesAsPlus comes before the set');
  CheckEqS('a b+', U.Encode('a b+', [], []), 'without SpacesAsPlus a space outside the set is copied');
  CheckEqS('%7F~', U.Encode(#127'~', [127], []), 'U+007F in the set; without it:');
  CheckEqS(#127'~', U.Encode(#127'~', [], []), 'U+007F outside the set is copied');
  CheckEqS('%E9%00%41%00', U.Encode(EAcute + 'A', [Ord('A')], [], TEncoding.Unicode), 'the bytes of the given encoding: UTF-16');

  Section('decoding (RTL-1213)');
  CheckEqS('=A' + EAcute + EAcute, Decoded(dfUrl, '%41%c3%A9%C3%a9'), 'hex digits of either case');
  CheckEqS('=a+b c', Decoded(dfUrl, 'a+b%20c'), 'URLDecode keeps "+"');
  CheckEqS('=a b c', Decoded(dfForm, 'a+b%20c'), 'FormDecode reads "+" as a space');
  CheckEqS('a b', U.Decode('a+b', [TURLEncoding.PlusAsSpaces]), 'Decode with PlusAsSpaces');
  CheckEqS('a+b', U.Decode('a+b', []), 'Decode without options');
  CheckEqS('=' + #$FFFD, Decoded(dfUrl, '%E9'), 'a byte that is not UTF-8 becomes U+FFFD, no exception');
  S := U.URLDecode('%00');
  Check((Length(S) = 1) and (S[1] = #0), '%00 is one character U+0000');
  CheckEqS('=' + EAcute + ' ' + Grin, Decoded(dfUrl, EAcute + '%20' + Grin), 'unescaped non-ASCII input is kept');
  CheckEqS('=' + #$FFFD, Decoded(dfUrl, #$D83D), 'an unescaped lone surrogate becomes U+FFFD');
  CheckEqS('?', U.FormDecode('%3F', TEncoding.ASCII), 'FormDecode with another encoding');
  CheckEqS('AB', U.Decode('%41%00B', [], TEncoding.Unicode), 'escaped bytes and the bytes of a character: UTF-16');
  CheckEqS('==', Decoded(dfUrl, '') + Decoded(dfForm, ''), 'empty text stays empty');
  CheckEqS(BadEscape + '1', Decoded(dfUrl, '%zz'), 'URLDecode(%zz) raises');
  CheckEqS(BadEscape + '3', Decoded(dfUrl, 'ab%4'), 'one hex digit at the end');
  CheckEqS(BadEscape + '4', Decoded(dfUrl, '100%'), 'a "%" at the end');
  CheckEqS(BadEscape + '1', Decoded(dfOneParameter, '%zz'), 'TNetEncoding.URL.Decode(%zz) raises the same');
  CheckEqS(BadEscape + '1', Decoded(dfForm, '%zz'), 'FormDecode(%zz)');
  CheckEqS(BadEscape + '1', Decoded(dfBytes, '%zz'), 'Decode(bytes) of %zz');
  CheckEqS(BadEscape + '4', Decoded(dfUrl, '%41%4G'), 'the second digit is not hex');
  CheckEqS(BadEscape + '1', Decoded(dfUrl, '%%41'), '"%" after "%"');
  CheckEqS(BadEscape + '2', Decoded(dfForm, '+%+1'), '"+" is not a hex digit; the position counts characters');
  CheckEqS(BadEscape + '3', Decoded(dfUrl, Grin + '%' + #$0661'1'), 'a digit outside ASCII is not a hex digit');

  Section('round trips (RTL-1212)');
  S := All + EAcute + Grin + #9;
  CheckEqS(S, U.URLDecode(U.EncodePath(S)), 'URLDecode(EncodePath(S)) = S');
  CheckEqS(S, U.URLDecode(U.EncodeAuth(S)), 'URLDecode(EncodeAuth(S)) = S');
  CheckEqS(S, U.URLDecode(U.EncodeQuery(S)), 'URLDecode(EncodeQuery(S)) = S');
  CheckEqS(S, U.FormDecode(U.EncodeForm(S)), 'FormDecode(EncodeForm(S)) = S');
  CheckEqS(S, U.Decode(U.Encode(S)), 'Decode(Encode(S)) = S');
  CheckEqS(CardQuery, U.Encode(U.Decode(CardQuery)), 'Encode(Decode(V)) = V for an encoded query component');
  CheckEqS('%2541', U.EncodeQuery('%41'), 'a "%" in raw text is encoded: decoding once gives the text back');

  Section('the inherited forms (RTL-1214, RTL-1203, RTL-1204)');
  CheckEqS(CardQuery, ToText(U.Encode(TEncoding.UTF8.GetBytes(Card))), 'Encode(bytes) reads UTF-8 text');
  CheckEqS(Card, TEncoding.UTF8.GetString(U.Decode(ToBytes(CardQuery))), 'Decode(bytes) returns the UTF-8 form');
  CheckEqS(CardQuery, U.EncodeBytesToString(TEncoding.UTF8.GetBytes(Card)), 'EncodeBytesToString(bytes)');
  P := TEncoding.UTF8.GetBytes(Card);
  CheckEqS(CardQuery, U.EncodeBytesToString(Pointer(P), Length(P)), 'EncodeBytesToString(pointer, size)');
  CheckEqS('a b&c=d/'#$C3#$A9, ToText(U.DecodeStringToBytes(CardQuery)), 'DecodeStringToBytes');
  Src := TMemoryStream.Create;
  Dst := TMemoryStream.Create;
  Src.WriteBuffer(P[0], Length(P));
  Src.Position := 0;
  CheckEqI(Length(CardQuery), U.Encode(Src, Dst), 'Encode(streams) returns the bytes written');
  CheckEqS(CardQuery, StreamText(Dst), 'and writes the bytes of the array form');
  Dst.Position := 0;
  Src.Clear;
  CheckEqI(10, U.Decode(Dst, Src), 'Decode(streams)');
  CheckEqS('a b&c=d/'#$C3#$A9, StreamText(Src), 'gives the UTF-8 form of the card text');
  Src.Clear;
  Dst.Clear;
  P := ToBytes('ab%4');
  Dst.WriteBuffer(P[0], Length(P));
  Dst.Position := 0;
  Expect('EConvertError', 'Invalid URL escape sequence at position 3');
  try U.Decode(Dst, Src); except on E: Exception do CheckRaises('Decode(streams) of a malformed escape', E.ClassName, E.Message); end;
  CheckEqI(0, Src.Size, 'nothing was written');
  Expect('EArgumentNilException', 'Argument must not be nil');
  try U.Encode(nil, Dst); except on E: Exception do CheckRaises('a nil stream', E.ClassName, E.Message); end;
  Src.Free;
  Dst.Free;

  Section('the shared instance (RTL-1202)');
  Check(TNetEncoding.URL = TNetEncoding.URL, 'URL returns the same object twice');
  CheckEqS('TURLEncoding', TNetEncoding.URL.ClassName, 'class of URL');
  Check(TNetEncoding.URL is TNetEncoding, 'TURLEncoding is a TNetEncoding');
  Own := TURLEncoding.Create;
  Check(Own <> TNetEncoding.URL, 'an object the program creates is its own');
  CheckEqS(CardForm, Own.EncodeForm(Card), 'and works the same');
  Own.Free;
end;

var
  Before: NativeInt;
begin
  U := TNetEncoding.URL;
  { the shared instance and the encodings live until the program ends: they exist before the count is taken }
  U.Decode(U.Encode('x'));
  TEncoding.ASCII.GetBytes('x');
  TEncoding.Unicode.GetBytes('x');
  Before := _LiveAllocCount;
  Run;
  CheckEqI(Before, _LiveAllocCount, 'no block is left');
  Done;
end.
