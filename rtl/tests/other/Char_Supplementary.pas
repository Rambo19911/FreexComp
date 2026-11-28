// phase: 8
// covers: RTL-1277, RTL-1278
program Char_Supplementary;
{ other-units.md section 5, task R-267 (F10-047): the supplementary planes in unit Character. The one-based (S, Index)
  forms classify the code point at Index (a surrogate pair as its code point, any other code unit as itself); an index
  outside the string raises EArgumentOutOfRangeException. Block B of standard-helpers.md section 5.2: the categories
  and classes of code points of every kind (ranges written as First / Last line pairs included), the string
  'a', U+1F600, 'b' index by index, the errors, ConvertToUtf32 with CharLength and the loop over code points, and a
  checksum over the categories of U+10000 .. U+10FFFF (the value printed by rtl/tools/GenCharTables.cs). }
uses
  SysUtils, Character, TestKit;

const
  ClassNames: array[0..10] of string = ('IsLetter', 'IsDigit', 'IsLetterOrDigit', 'IsWhiteSpace', 'IsUpper', 'IsLower',
    'IsControl', 'IsPunctuation', 'IsSymbol', 'IsSeparator', 'IsNumber');

{ the names of the classes that are True at S[Index], separated by spaces ('none' when no class is True) }
function TrueClasses(const S: string; Index: Integer): string;
var
  B: array[0..10] of Boolean;
  I: Integer;
begin
  B[0] := IsLetter(S, Index);
  B[1] := IsDigit(S, Index);
  B[2] := IsLetterOrDigit(S, Index);
  B[3] := IsWhiteSpace(S, Index);
  B[4] := IsUpper(S, Index);
  B[5] := IsLower(S, Index);
  B[6] := IsControl(S, Index);
  B[7] := IsPunctuation(S, Index);
  B[8] := IsSymbol(S, Index);
  B[9] := IsSeparator(S, Index);
  B[10] := IsNumber(S, Index);
  Result := '';
  for I := 0 to 10 do
    if B[I] then
    begin
      if Result <> '' then
        Result := Result + ' ';
      Result := Result + ClassNames[I];
    end;
  if Result = '' then
    Result := 'none';
end;

procedure Row(CodePoint: Cardinal; Expected: TUnicodeCategory; const ExpectedClasses: string);
var
  S: string;
begin
  S := ConvertFromUtf32(CodePoint);
  CheckEqI(Ord(Expected), Ord(GetUnicodeCategory(S, 1)), 'category U+' + IntToHex(CodePoint, 4));
  CheckEqS(ExpectedClasses, TrueClasses(S, 1), 'classes U+' + IntToHex(CodePoint, 4));
end;

procedure TestCodePoints;
begin
  Section('code points');
  Row($1D400, ucUppercaseLetter, 'IsLetter IsLetterOrDigit IsUpper');
  Row($10428, ucLowercaseLetter, 'IsLetter IsLetterOrDigit IsLower');
  Row($10400, ucUppercaseLetter, 'IsLetter IsLetterOrDigit IsUpper');
  Row($1D7CE, ucDecimalNumber, 'IsDigit IsLetterOrDigit IsNumber');
  Row($10140, ucLetterNumber, 'IsNumber');
  Row($1F100, ucOtherNumber, 'IsNumber');
  Row($1F600, ucOtherSymbol, 'IsSymbol');
  Row($10000, ucOtherLetter, 'IsLetter IsLetterOrDigit');
  Row($20000, ucOtherLetter, 'IsLetter IsLetterOrDigit');
  Row($2A6DF, ucOtherLetter, 'IsLetter IsLetterOrDigit');
  Row($30000, ucOtherLetter, 'IsLetter IsLetterOrDigit');
  Row($16FE0, ucModifierLetter, 'IsLetter IsLetterOrDigit');
  Row($E0001, ucFormat, 'none');
  Row($E0100, ucNonSpacingMark, 'none');
  Row($F0000, ucPrivateUse, 'none');
  Row($10FFFD, ucPrivateUse, 'none');
  Row($2A6E0, ucUnassigned, 'none');
  Row($1FFFE, ucUnassigned, 'none');
  Row($10FFFE, ucUnassigned, 'none');
  Row($10FFFF, ucUnassigned, 'none');
  { a BMP code unit through the (S, Index) form is the Char form }
  Row(Ord('A'), ucUppercaseLetter, 'IsLetter IsLetterOrDigit IsUpper');
  Row(9, ucControl, 'IsWhiteSpace IsControl');
  Row($3000, ucSpaceSeparator, 'IsWhiteSpace IsSeparator');
end;

procedure TestCase;
begin
  Section('case of supplementary code points');
  CheckEqS(ConvertFromUtf32($10400), ToUpper(ConvertFromUtf32($10428)), 'ToUpper U+10428');
  CheckEqS(ConvertFromUtf32($10428), ToLower(ConvertFromUtf32($10400)), 'ToLower U+10400');
  CheckEqS(ConvertFromUtf32($1E900), ToUpper(ConvertFromUtf32($1E922)), 'ToUpper U+1E922');
  CheckEqS(ConvertFromUtf32($1E922), ToLower(ConvertFromUtf32($1E900)), 'ToLower U+1E900');
  CheckEqS('A' + ConvertFromUtf32($10400) + ConvertFromUtf32($1F600) + 'B',
    ToUpper('a' + ConvertFromUtf32($10428) + ConvertFromUtf32($1F600) + 'b'), 'ToUpper mixed string');
  CheckEqS(ConvertFromUtf32($1D400), ToLower(ConvertFromUtf32($1D400)), 'ToLower U+1D400: no mapping');
end;

procedure TestPairString;
var
  S: string;
begin
  Section('a, U+1F600, b');
  S := 'a' + #$D83D + #$DE00 + 'b';
  CheckEqB(True, IsLetter(S, 1), 'IsLetter(S, 1)');
  CheckEqB(True, IsSymbol(S, 2), 'IsSymbol(S, 2)');
  CheckEqI(Ord(ucOtherSymbol), Ord(GetUnicodeCategory(S, 2)), 'GetUnicodeCategory(S, 2)');
  CheckEqI(Ord(ucSurrogate), Ord(GetUnicodeCategory(S, 3)), 'GetUnicodeCategory(S, 3): low surrogate');
  CheckEqB(False, IsSymbol(S, 3), 'IsSymbol(S, 3)');
  CheckEqS('none', TrueClasses(S, 3), 'classes at the low surrogate');
  CheckEqB(True, IsLetter(S, 4), 'IsLetter(S, 4)');
  CheckEqB(True, IsSurrogatePair(S, 2), 'IsSurrogatePair(S, 2)');
  CheckEqB(False, IsSurrogatePair(S, 1), 'IsSurrogatePair(S, 1)');
  CheckEqB(False, IsSurrogatePair(S, 3), 'IsSurrogatePair(S, 3)');
  CheckEqB(False, IsSurrogatePair(S, 4), 'IsSurrogatePair(S, 4): last index');
  CheckEqB(True, IsHighSurrogate(S, 2), 'IsHighSurrogate(S, 2)');
  CheckEqB(False, IsHighSurrogate(S, 3), 'IsHighSurrogate(S, 3)');
  CheckEqB(True, IsLowSurrogate(S, 3), 'IsLowSurrogate(S, 3)');
  CheckEqB(False, IsLowSurrogate(S, 2), 'IsLowSurrogate(S, 2)');
  CheckEqB(False, IsSurrogate(S, 1), 'IsSurrogate(S, 1)');
  CheckEqB(True, IsSurrogate(S, 2), 'IsSurrogate(S, 2)');
  CheckEqB(True, IsSurrogate(S, 3), 'IsSurrogate(S, 3)');
  { the Char forms are unchanged: a surrogate code unit is never a letter, symbol, ... }
  CheckEqB(False, IsLetter(#$D83D), 'IsLetter(#$D83D)');
  CheckEqB(False, IsSymbol(#$D83D), 'IsSymbol(#$D83D)');
  CheckEqI(Ord(ucSurrogate), Ord(GetUnicodeCategory(#$D83D)), 'GetUnicodeCategory(#$D83D)');
  S := 'a' + #$D83D;
  CheckEqI(Ord(ucSurrogate), Ord(GetUnicodeCategory(S, 2)), 'lone high surrogate at the end');
  CheckEqB(False, IsSurrogatePair(S, 2), 'IsSurrogatePair at a lone high surrogate');
  S := #$D83D + 'x';
  CheckEqI(Ord(ucSurrogate), Ord(GetUnicodeCategory(S, 1)), 'high surrogate before a letter');
  CheckEqB(True, IsLetter(S, 2), 'the letter after it');
end;

procedure ExpectError;
begin
  Expect('EArgumentOutOfRangeException', 'Argument out of range');
end;

procedure TestErrors;
var
  S: string;
  N: Integer;
  C: Cardinal;
begin
  Section('errors');
  S := 'a' + #$D83D + #$DE00 + 'b';
  ExpectError;
  try GetUnicodeCategory(S, 0); except on E: Exception do CheckRaises('GetUnicodeCategory(S, 0)', E.ClassName, E.Message); end;
  ExpectError;
  try GetUnicodeCategory(S, 5); except on E: Exception do CheckRaises('GetUnicodeCategory(S, 5)', E.ClassName, E.Message); end;
  ExpectError;
  try IsLetter(S, 0); except on E: Exception do CheckRaises('IsLetter(S, 0)', E.ClassName, E.Message); end;
  ExpectError;
  try IsWhiteSpace(S, 5); except on E: Exception do CheckRaises('IsWhiteSpace(S, 5)', E.ClassName, E.Message); end;
  ExpectError;
  try IsHighSurrogate(S, 0); except on E: Exception do CheckRaises('IsHighSurrogate(S, 0)', E.ClassName, E.Message); end;
  ExpectError;
  try IsSurrogate(S, 5); except on E: Exception do CheckRaises('IsSurrogate(S, 5)', E.ClassName, E.Message); end;
  ExpectError;
  try IsSurrogatePair(S, 0); except on E: Exception do CheckRaises('IsSurrogatePair(S, 0)', E.ClassName, E.Message); end;
  ExpectError;
  try IsSurrogatePair(S, 5); except on E: Exception do CheckRaises('IsSurrogatePair(S, 5)', E.ClassName, E.Message); end;
  ExpectError;
  try IsDigit('', 1); except on E: Exception do CheckRaises('IsDigit('''', 1)', E.ClassName, E.Message); end;
  S := 'a' + #$D83D;
  ExpectError;
  try ConvertToUtf32(S, 2); except on E: Exception do CheckRaises('ConvertToUtf32 at a lone high surrogate', E.ClassName, E.Message); end;
  ExpectError;
  try ConvertToUtf32('a' + #$D83D + #$DE00, 3, N); except on E: Exception do CheckRaises('ConvertToUtf32 with CharLength at a low surrogate', E.ClassName, E.Message); end;
  ExpectError;
  try ConvertToUtf32('a', 0, N); except on E: Exception do CheckRaises('ConvertToUtf32 with CharLength, index 0', E.ClassName, E.Message); end;
  ExpectError;
  try C := ConvertToUtf32('a', 'b'); except on E: Exception do CheckRaises('ConvertToUtf32(''a'', ''b'')', E.ClassName, E.Message); end;
  ExpectError;
  try C := ConvertToUtf32(#$DE00, #$D83D); except on E: Exception do CheckRaises('ConvertToUtf32 of a reversed pair', E.ClassName, E.Message); end;
end;

procedure TestUtf32;
var
  S: string;
  N, I, Count: Integer;
  Sum: Cardinal;
begin
  Section('ConvertToUtf32 and CharLength');
  S := 'a' + #$D83D + #$DE00 + 'b';
  CheckEqI($1F600, ConvertToUtf32(S, 2, N), 'ConvertToUtf32(S, 2, N)');
  CheckEqI(2, N, 'N at the pair');
  CheckEqI(97, ConvertToUtf32(S, 1, N), 'ConvertToUtf32(S, 1, N)');
  CheckEqI(1, N, 'N at a BMP code unit');
  CheckEqI(98, ConvertToUtf32(S, 4, N), 'ConvertToUtf32(S, 4, N)');
  CheckEqI(1, N, 'N at the last code unit');
  CheckEqI($1F600, ConvertToUtf32(#$D83D, #$DE00), 'ConvertToUtf32(#$D83D, #$DE00)');
  CheckEqI($10000, ConvertToUtf32(#$D800, #$DC00), 'ConvertToUtf32 first pair');
  CheckEqI($10FFFF, ConvertToUtf32(#$DBFF, #$DFFF), 'ConvertToUtf32 last pair');
  { a loop over the code points of S by CharLength }
  Count := 0;
  Sum := 0;
  I := 1;
  while I <= Length(S) do
  begin
    Sum := Sum + ConvertToUtf32(S, I, N);
    Inc(Count);
    I := I + N;
  end;
  CheckEqI(3, Count, 'loop by CharLength: code points');
  CheckEqI(97 + $1F600 + 98, Sum, 'loop by CharLength: sum');
end;

{$Q-}
{$R-}
procedure TestChecksum;
var
  C: Cardinal;
  Sum: Cardinal;
  S: string;
begin
  Section('supplementary planes');
  SetLength(S, 2);
  Sum := 0;
  for C := $10000 to $10FFFF do
  begin
    S[1] := Char($D800 + (C - $10000) shr 10);
    S[2] := Char($DC00 + (C - $10000) and $3FF);
    Sum := Sum + Cardinal(Ord(GetUnicodeCategory(S, 1))) * (C mod 251 + 1);
  end;
  CheckEqI(3279368517, Sum, 'GetUnicodeCategory checksum U+10000 .. U+10FFFF');
end;

begin
  TestCodePoints;
  TestCase;
  TestPairString;
  TestErrors;
  TestUtf32;
  TestChecksum;
  Done;
end.
