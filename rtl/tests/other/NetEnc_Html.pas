// phase: 8
// covers: RTL-1215, RTL-1216, RTL-1218
program NetEnc_Html;
{ NetEncoding, HTML character escaping (rtl/phase8/encodings-and-hashes.md section 3.5, vectors 6.2 G): Encode
  replaces five characters and nothing else, the apostrophe as a numeric reference; Decode reads named, decimal and
  hexadecimal references in one pass, needs the ";", compares names case-sensitively, copies what it does not
  recognise and never raises; the numeric conversions (0, surrogates, values above U+10FFFF, the table for 80 to 9F,
  surrogate pairs above U+FFFF); the byte and stream forms; the shared instance. }
uses
  SysUtils, Classes, NetEncoding, TestKit;

const
  EAcute = #$00E9;
  Grin = #$D83D#$DE00;               { U+1F600 as its surrogate pair }
  Repl = #$FFFD;
  CardRaw = '<a href="x">&amp;</a>';
  CardCoded = '&lt;a href=&quot;x&quot;&gt;&amp;amp;&lt;/a&gt;';
  { what a numeric reference to 80..9F becomes; 0 marks the five values that stay as they are }
  C1: array[$80..$9F] of Word = (
    $20AC, 0, $201A, $0192, $201E, $2026, $2020, $2021, $02C6, $2030, $0160, $2039, $0152, 0, $017D, 0,
    0, $2018, $2019, $201C, $201D, $2022, $2013, $2014, $02DC, $2122, $0161, $203A, $0153, 0, $017E, $0178);
  { references that are not complete or not known: each is copied as it stands }
  Unchanged: array[0..22] of string = (
    '&unknown; &amp &AMP; &#; &#x; &#xZZ; &#12 & &; &', 'a&b', '&', '&&', '&;', '&#', '&#x', '&#65', '&#x41', '&lt',
    '&LT;', '&Amp;', '&# 65;', '&#-65;', '&#+65;', '&#65x;', '&#xG1;', '&#x 41;', '&#x41 ;', '&lt ;', '& lt;',
    '&#x;41;', '&eacute');

var
  H: TNetEncoding;

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

function StreamText(M: TMemoryStream): string;
var
  A: TBytes;
begin
  SetLength(A, Integer(M.Size));
  if Length(A) > 0 then
    Move(M.Memory^, A[0], Length(A));
  Result := ToText(A);
end;

{ the code units of S in hex, separated by spaces: keeps the expected and the actual value readable in ASCII }
function Units(const S: string): string;
var
  I: Integer;
begin
  Result := '';
  for I := 1 to Length(S) do
  begin
    if I > 1 then
      Result := Result + ' ';
    Result := Result + IntToHex(Ord(S[I]), 4);
  end;
end;

procedure Decodes(const Coded, Expected, Name: string);
begin
  CheckEqS(Units(Expected), Units(H.Decode(Coded)), Name);
end;

procedure Run;
var
  I, Rows, Gaps: Integer;
  S: string;
  Want: Char;
  Own: THTMLEncoding;
  P: TBytes;
  Src, Dst: TMemoryStream;
begin
  Section('Encode: five characters (RTL-1215)');
  CheckEqS(CardCoded, H.Encode(CardRaw), 'the card vector');
  CheckEqS(CardRaw, H.Decode(CardCoded), 'and back');
  CheckEqS('it&#39;s', H.Encode('it''s'), 'the apostrophe is written as &#39;');
  CheckEqS('&amp;&lt;&gt;&quot;&#39;', H.Encode('&<>"'''), 'the five, side by side');
  S := EAcute + Grin + #9;
  CheckEqS(Units(S), Units(H.Encode(S)), 'non-ASCII text and a tab are copied');
  S := #0#1#31#127#$00A0#$FFFF#$D83D;
  CheckEqS(Units(S), Units(H.Encode(S)), 'control characters and a lone surrogate are copied: no numeric reference');
  CheckEqS('', H.Encode('') + H.Decode(''), 'empty text stays empty');
  CheckEqS('&amp;amp;', H.Encode('&amp;'), 'text that is encoded already is encoded again');
  S := 'x < y && y > "z" ' + EAcute + ' it''s';
  CheckEqS(S, H.Decode(H.Encode(S)), 'Decode(Encode(S)) = S');

  Section('Decode: named references (RTL-1216)');
  Decodes('&lt;&gt;&amp;&quot;&apos;&#39;', '<>&"''''', 'the markup characters, &apos; included');
  Decodes('&nbsp;&copy;&eacute;&Eacute;&euro;&hearts;&thetasym;&sup1;&frac14;&there4;',
    #$00A0#$00A9#$00E9#$00C9#$20AC#$2665#$03D1#$00B9#$00BC#$2234, 'ten names');
  Decodes('&amp;lt;', '&lt;', 'replaced text is not scanned again');
  Decodes('&amp;amp;amp;', '&amp;amp;', 'one level only');
  Decodes('a&lt;b&gt;c', 'a<b>c', 'text around references');
  Decodes('&&lt;&', '&<&', 'a "&" before and after a reference');
  Decodes('&eacute;&Eacute;&EACUTE;', #$00E9#$00C9'&EACUTE;', 'names are case-sensitive');
  Decodes('&ltx;&lt;x;', '&ltx;<x;', 'the name ends at the ";"');

  Section('Decode: numeric references (RTL-1216, RTL-1218)');
  S := H.Decode('&#x1F600;');
  Check((Length(S) = 2) and (S[1] = #$D83D) and (S[2] = #$DE00), '&#x1F600; is the surrogate pair D83D DE00');
  Decodes('&#128512;', Grin, 'the same in decimal');
  Decodes('&#65;&#065;&#x41;&#X41;&#x00041;', 'AAAAA', 'decimal, hex with "x" and "X", leading zeros');
  Decodes('&#xaB;&#XAb;', #$00AB#$00AB, 'hex digits of either case');
  Decodes('&#0;', Repl, '0 is U+FFFD');
  Decodes('&#x0;&#00;', Repl + Repl, '0 in other spellings');
  Decodes('&#x110000;', Repl, 'above U+10FFFF is U+FFFD');
  Decodes('&#xD800;', Repl, 'D800 is U+FFFD');
  Decodes('&#xDFFF;', Repl, 'DFFF is U+FFFD');
  Decodes('&#99999999999999999999;', Repl, 'a number of any length is read: too large is U+FFFD');
  Decodes('&#4294967361;&#x100000041;', Repl + Repl, 'a value that would wrap to 65 in 32 bits is too large, not "A"');
  Decodes('&#18446744073709551681;&#x10000000000000041;', Repl + Repl, 'nor in 64 bits');
  Decodes('&#0000000000000000000000065;', 'A', 'leading zeros do not make a value too large');
  Decodes('&#x10FFFF;', #$DBFF#$DFFF, 'U+10FFFF is the last code point');
  Decodes('&#x10000;', #$D800#$DC00, 'U+10000 is the first pair');
  Decodes('&#xD7FF;&#xE000;', #$D7FF#$E000, 'the neighbours of the surrogates');
  Decodes('&#xFFFF;&#xFFFE;&#xFDD0;', #$FFFF#$FFFE#$FDD0, 'noncharacters are kept');
  Decodes('&#1;&#x7F;&#13;', #1#$7F#13, 'control characters are kept');
  Decodes('&#150;', #$2013, '&#150; is U+2013');
  Decodes('&#x80;', #$20AC, '&#x80; is U+20AC');
  Decodes('&#x81;', #$0081, '&#x81; stays U+0081');
  Decodes('&#x7F;&#xA0;', #$007F#$00A0, 'the neighbours of 80..9F');

  Section('the table for 80..9F (RTL-1218)');
  Rows := 0;
  Gaps := 0;
  for I := $80 to $9F do
  begin
    if C1[I] <> 0 then
    begin
      Want := Char(C1[I]);
      Inc(Rows);
    end
    else
    begin
      Want := Char(I);
      Inc(Gaps);
    end;
    S := H.Decode('&#' + IntToStr(I) + ';') + H.Decode('&#x' + IntToHex(I, 2) + ';') + H.Decode('&#X' + LowerCase(IntToHex(I, 2)) + ';');
    CheckEqS(Units(Want + Want + Want), Units(S), IntToHex(I, 2) + ' is U+' + IntToHex(Ord(Want), 4));
  end;
  CheckEqI(27, Rows, 'rows');
  CheckEqI(5, Gaps, 'values without a row');

  Section('what is not a reference is copied (RTL-1216)');
  for I := Low(Unchanged) to High(Unchanged) do
    CheckEqS(Unchanged[I], H.Decode(Unchanged[I]), 'unchanged: ' + Unchanged[I]);
  Decodes('&#'#$0661';&#x'#$FF11';', '&#'#$0661';&#x'#$FF11';', 'a digit outside ASCII is not a digit');
  Decodes('&#65;&#x;&#66;&bogus;&lt;&#;', 'A&#x;B&bogus;<&#;', 'scanning goes on after a "&" that is copied');
  Decodes('&#65&#66;', '&#65B', 'a reference without ";" in front of a complete one');
  Decodes('&&&amp;&&', '&&&&&', 'runs of "&"');

  Section('the inherited forms (RTL-1203, RTL-1204)');
  CheckEqS('&lt;'#$C3#$A9'&gt;', ToText(H.Encode(ToBytes('<'#$C3#$A9'>'))), 'Encode(bytes) reads UTF-8 text');
  CheckEqS('<'#$C3#$A9'>', ToText(H.Decode(ToBytes('&lt;&eacute;&gt;'))), 'Decode(bytes) returns the UTF-8 form');
  CheckEqS('&lt;' + EAcute, H.EncodeBytesToString(ToBytes('<'#$C3#$A9)), 'EncodeBytesToString(bytes)');
  P := ToBytes('<>x');
  CheckEqS('&lt;&gt;', H.EncodeBytesToString(Pointer(P), 2), 'EncodeBytesToString(pointer, size)');
  CheckEqS(#$F0#$9F#$98#$80, ToText(H.DecodeStringToBytes('&#x1F600;')), 'DecodeStringToBytes: one 4-byte sequence');
  Src := TMemoryStream.Create;
  Dst := TMemoryStream.Create;
  P := ToBytes(CardRaw);
  Src.WriteBuffer(P[0], Length(P));
  Src.Position := 0;
  CheckEqI(Length(CardCoded), H.Encode(Src, Dst), 'Encode(streams) returns the bytes written');
  CheckEqS(CardCoded, StreamText(Dst), 'and writes the bytes of the array form');
  Dst.Position := 0;
  Src.Clear;
  CheckEqI(Length(CardRaw), H.Decode(Dst, Src), 'Decode(streams)');
  CheckEqS(CardRaw, StreamText(Src), 'gives the card text back');
  Src.Free;
  Dst.Free;

  Section('the shared instance (RTL-1202)');
  Check(TNetEncoding.HTML = TNetEncoding.HTML, 'HTML returns the same object twice');
  CheckEqS('THTMLEncoding', TNetEncoding.HTML.ClassName, 'class of HTML');
  Own := THTMLEncoding.Create;
  Check(Own <> TNetEncoding.HTML, 'an object the program creates is its own');
  CheckEqS('&lt;', Own.Encode('<'), 'and works the same');
  Own.Free;
end;

var
  Before: NativeInt;
begin
  H := TNetEncoding.HTML;
  { the shared instance and the UTF-8 encoding live until the program ends: they exist before the count is taken }
  H.Decode(H.Encode(TEncoding.UTF8.GetString(TEncoding.UTF8.GetBytes('x'))));
  Before := _LiveAllocCount;
  Run;
  CheckEqI(Before, _LiveAllocCount, 'no block is left');
  Done;
end.
