// covers: DAT-110
program SqlUnicode;
{ Text crossing the SQLite API. Helper section: the UTF-16 <-> UTF-8 conversion used for SQL text, file
  names, identifiers and messages. File names and identifiers: a database file and a table whose names
  are not ASCII. }
uses
  SysUtils, Classes, SQLite3API, SQLiteData, DataTestUtils;

function HexBytes(const B: TBytes): string;
var
  I: Integer;
begin
  Result := '';
  for I := 0 to Length(B) - 1 do
  begin
    if I > 0 then
      Result := Result + ' ';
    Result := Result + IntToHex(B[I], 2);
  end;
end;

function HexUnits(const S: string): string;
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

{ Decodes the given bytes (a zero terminator is added here). }
function Decode(const Bytes: array of Byte): string;
var
  B: TBytes;
  I: Integer;
begin
  SetLength(B, Length(Bytes) + 1);
  for I := 0 to Length(Bytes) - 1 do
    B[I] := Bytes[I];
  B[Length(Bytes)] := 0;
  Result := HexUnits(Utf8ToUtf16(PUtf8(@B[0])));
end;

procedure TestEncode;
begin
  Section('UTF-16 to UTF-8');
  CheckEq(HexBytes(Utf16ToUtf8('')), '00', 'an empty string is the terminator alone');
  CheckEq(HexBytes(Utf16ToUtf8('abc')), '61 62 63 00', 'ASCII, zero-terminated');
  CheckEq(HexBytes(Utf16ToUtf8(#$007F#$0080)), '7F C2 80 00', 'the one-byte and two-byte boundary');
  CheckEq(HexBytes(Utf16ToUtf8(#$00E9)), 'C3 A9 00', 'two bytes');
  CheckEq(HexBytes(Utf16ToUtf8(#$07FF#$0800)), 'DF BF E0 A0 80 00', 'the two-byte and three-byte boundary');
  CheckEq(HexBytes(Utf16ToUtf8(#$20AC)), 'E2 82 AC 00', 'three bytes');
  CheckEq(HexBytes(Utf16ToUtf8(#$FFFF)), 'EF BF BF 00', 'the last three-byte character');
  CheckEq(HexBytes(Utf16ToUtf8(#$D83D#$DE00)), 'F0 9F 98 80 00', 'a surrogate pair is four bytes');
  CheckEq(HexBytes(Utf16ToUtf8(#$D800#$DC00)), 'F0 90 80 80 00', 'the first supplementary character');
  CheckEq(HexBytes(Utf16ToUtf8(#$DBFF#$DFFF)), 'F4 8F BF BF 00', 'the last supplementary character');
  CheckEq(HexBytes(Utf16ToUtf8('a' + #$D83D)), '61 EF BF BD 00', 'an unpaired high surrogate at the end');
  CheckEq(HexBytes(Utf16ToUtf8(#$D83D + 'a')), 'EF BF BD 61 00', 'an unpaired high surrogate before a character');
  CheckEq(HexBytes(Utf16ToUtf8(#$DE00 + 'a')), 'EF BF BD 61 00', 'an unpaired low surrogate');
  CheckEq(HexBytes(Utf16ToUtf8(#$DE00#$D83D)), 'EF BF BD EF BF BD 00', 'a reversed pair is two replacement characters');
  CheckEq(HexBytes(Utf16ToUtf8(#$D83D#$D83D#$DE00)), 'EF BF BD F0 9F 98 80 00', 'a high surrogate before a pair');
  CheckEq(HexBytes(Utf16ToUtf8('a' + #0 + 'b')), '61 00 62 00', 'an embedded #0 is kept');
end;

procedure TestDecode;
begin
  Section('UTF-8 to UTF-16');
  CheckEq(Utf8ToUtf16(nil), '', 'nil is the empty string');
  CheckEq(Decode([]), '', 'an empty text');
  CheckEq(Decode([$61, $62, $63]), '0061 0062 0063', 'ASCII');
  CheckEq(Decode([$C3, $A9]), '00E9', 'two bytes');
  CheckEq(Decode([$7F, $C2, $80, $DF, $BF]), '007F 0080 07FF', 'the two-byte range');
  CheckEq(Decode([$E0, $A0, $80, $E2, $82, $AC, $EF, $BF, $BF]), '0800 20AC FFFF', 'the three-byte range');
  CheckEq(Decode([$ED, $9F, $BF, $EE, $80, $80]), 'D7FF E000', 'around the surrogate range');
  CheckEq(Decode([$F0, $9F, $98, $80]), 'D83D DE00', 'four bytes become a surrogate pair');
  CheckEq(Decode([$F0, $90, $80, $80, $F4, $8F, $BF, $BF]), 'D800 DC00 DBFF DFFF', 'the four-byte range');

  Section('invalid UTF-8');
  CheckEq(Decode([$80]), 'FFFD', 'a lone continuation byte');
  CheckEq(Decode([$61, $80, $BF, $62]), '0061 FFFD FFFD 0062', 'each stray continuation byte is one replacement');
  CheckEq(Decode([$C3, $61]), 'FFFD 0061', 'a lead byte without its continuation');
  CheckEq(Decode([$C3]), 'FFFD', 'a lead byte at the end');
  CheckEq(Decode([$E2, $82, $61]), 'FFFD 0061', 'a truncated three-byte sequence is one maximal subpart');
  CheckEq(Decode([$E2, $82]), 'FFFD', 'a three-byte sequence cut by the end');
  CheckEq(Decode([$F0, $9F, $98, $61]), 'FFFD 0061', 'a truncated four-byte sequence is one maximal subpart');
  CheckEq(Decode([$F0, $9F, $C3, $A9]), 'FFFD 00E9', 'decoding continues at the offending byte');
  CheckEq(Decode([$C0, $80]), 'FFFD FFFD', 'an overlong two-byte form');
  CheckEq(Decode([$C1, $BF]), 'FFFD FFFD', 'the other overlong lead byte');
  CheckEq(Decode([$E0, $80, $80]), 'FFFD FFFD FFFD', 'an overlong three-byte form');
  CheckEq(Decode([$E0, $9F, $BF]), 'FFFD FFFD FFFD', 'the last overlong three-byte form');
  CheckEq(Decode([$ED, $A0, $80]), 'FFFD FFFD FFFD', 'an encoded surrogate');
  CheckEq(Decode([$F0, $80, $80, $80]), 'FFFD FFFD FFFD FFFD', 'an overlong four-byte form');
  CheckEq(Decode([$F4, $90, $80, $80]), 'FFFD FFFD FFFD FFFD', 'beyond U+10FFFF');
  CheckEq(Decode([$F5, $80]), 'FFFD FFFD', 'a byte that is never valid');
  CheckEq(Decode([$FF, $FE]), 'FFFD FFFD', 'FF and FE');
end;

procedure TestNames;
var
  C: TSQLiteConnection;
  L: TStringList;
  FileName, Table: string;
begin
  Section('file names and identifiers');
  { Latin, Greek, CJK and a supplementary character in the file name and in the table name }
  FileName := GetCurrentDir + '\d' + #$00E9 + #$03A9 + #$65E5#$672C + #$D83D#$DE00 + '.db';
  Table := 't' + #$00E9 + #$65E5 + #$D83D#$DE00;
  C := TSQLiteConnection.Create(nil);
  L := TStringList.Create;
  C.Database := FileName;
  C.Open;
  Check(FileExists(FileName), 'the database file has the UTF-16 name');
  C.ExecSQL('CREATE TABLE ' + SQLiteQuoteIdent(Table) + ' (' + SQLiteQuoteIdent('c' + #$00FC) + ' TEXT)');
  C.GetTableNames(L);
  CheckEq(HexUnits(L.Text), HexUnits(Table + sLineBreak), 'the table name comes back unchanged');
  C.GetFieldNames(Table, L);
  CheckEq(HexUnits(L.Text), HexUnits('c' + #$00FC + sLineBreak), 'the column name comes back unchanged');
  C.Close;
  C.Open;
  C.GetTableNames(L);
  CheckEq(HexUnits(L.Text), HexUnits(Table + sLineBreak), 'and is stored in the file');
  C.Free;
  L.Free;
  Check(DeleteFile(FileName), 'the file is deleted');
end;

begin
  TestEncode;
  TestDecode;
  TestNames;
  WriteLn('failed checks: ', FailedChecks);
end.
