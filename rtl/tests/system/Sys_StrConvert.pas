// covers: RTL-070, RTL-072, RTL-073, RTL-078, ABI-021, ABI-025, ABI-027
program Sys_StrConvert;
{$POINTERMATH ON}
{ AnsiString family and ShortString helpers: UTF-16 <-> UTF-8 conversion (surrogate pairs, lone surrogates as EF BF BD,
  invalid bytes as U+FFFD), sharing and copy-on-write of byte strings, the header queries for every string kind,
  UTF8Encode / UTF8ToString / UTF8Decode, the code page of RawByteString blocks and of concatenations, ShortString
  truncation at a UTF-8 sequence boundary, char conversions, PAnsiChar, SetLength and Copy; nothing leaks. }
type
  TS5 = string[5];
  TS3 = string[3];

const
  Bad: array[0..13] of Byte = ($C0, $80, $E2, $82, $41, $ED, $A0, $80, $F4, $90, $80, $80, $FF, $C3);

var
  Base: NativeInt;

{ header fields of a byte string variable (rtl-abi.md section 3.1), 0 for ''; passed as Pointer(S), so no temporary holds a reference; the RawByteString overloads of StringRefCount and
  friends wait for the compiler's overload ranking }
function RC(P: Pointer): Integer;
begin
  Result := 0;
  if P <> nil then
    Result := PInteger(PByte(P) - 16)^;
end;

function CP(P: Pointer): Integer;
begin
  Result := 0;
  if P <> nil then
    Result := PWord(PByte(P) - 8)^;
end;

function ES(P: Pointer): Integer;
begin
  Result := 0;
  if P <> nil then
    Result := PWord(PByte(P) - 6)^;
end;

function HexDigit(V: Integer): Char;
begin
  if V < 10 then
    Result := Chr(Ord('0') + V)
  else
    Result := Chr(Ord('A') + V - 10);
end;

function ByteHex(const A: AnsiString): string;
var
  I: Integer;
begin
  Result := '';
  for I := 1 to Length(A) do
  begin
    if I > 1 then
      Result := Result + ' ';
    Result := Result + HexDigit(Ord(A[I]) shr 4) + HexDigit(Ord(A[I]) and 15);
  end;
end;

function ShortHex(const H: ShortString): string;
var
  I: Integer;
begin
  Result := '';
  for I := 1 to Length(H) do
  begin
    if I > 1 then
      Result := Result + ' ';
    Result := Result + HexDigit(Ord(H[I]) shr 4) + HexDigit(Ord(H[I]) and 15);
  end;
end;

function UnitHex(const S: string): string;
var
  I: Integer;
begin
  Result := '';
  for I := 1 to Length(S) do
  begin
    if I > 1 then
      Result := Result + ' ';
    Result := Result + HexDigit(Ord(S[I]) shr 12) + HexDigit((Ord(S[I]) shr 8) and 15) +
      HexDigit((Ord(S[I]) shr 4) and 15) + HexDigit(Ord(S[I]) and 15);
  end;
end;

{ temporaries live until the end of their routine, so the work runs in a procedure of its own }
procedure Run;
var
  S, T: string;
  A, B, E: AnsiString;
  U: UTF8String;
  R, R2: RawByteString;
  H5: TS5;
  H3: TS3;
  H: ShortString;
  C: Char;
  AC: AnsiChar;
  P: PAnsiChar;
  I: Integer;
begin
  { UTF-16 -> UTF-8 -> UTF-16 }
  S := 'A' + #$E9 + #$20AC + #$D83D + #$DE00;
  A := AnsiString(S);
  Writeln(Length(S), ' ', Length(A), ' ', ByteHex(A));
  T := string(A);
  Writeln(Length(T), ' ', T = S, ' ', UnitHex(T));
  S := #$D800 + 'x' + #$DC00 + #$DBFF;
  Writeln(ByteHex(AnsiString(S)));
  S := #$D800 + #$D800 + #$DC00;
  Writeln(ByteHex(AnsiString(S)));

  { invalid UTF-8: every byte that does not start a complete sequence is one U+FFFD }
  SetLength(A, 14);
  for I := 0 to 13 do
    A[I + 1] := AnsiChar(Bad[I]);
  T := string(A);
  Writeln(Length(T), ' ', UnitHex(T));

  { sharing, copy-on-write and the header queries }
  A := 'lit';
  Writeln(RC(Pointer(A)), ' ', CP(Pointer(A)), ' ', ES(Pointer(A)));
  B := A;
  B[1] := 'L';
  Writeln(A, ' ', B, ' ', RC(Pointer(B)));
  A := B;
  Writeln(RC(Pointer(A)), ' ', RC(Pointer(B)));
  A[2] := 'I';
  Writeln(A, ' ', B, ' ', RC(Pointer(A)), ' ', RC(Pointer(B)));
  A := '';
  Writeln(RC(Pointer(A)), ' ', CP(Pointer(A)), ' ', ES(Pointer(A)));
  S := '';
  Writeln(StringRefCount(S), ' ', StringCodePage(S), ' ', StringElementSize(S));
  S := 'w';
  Writeln(StringCodePage(S), ' ', StringElementSize(S));
  S := 'x' + #$E9;
  U := UTF8String(S);
  Writeln(CP(Pointer(U)), ' ', ES(Pointer(U)), ' ', Length(U), ' ', ByteHex(U));

  { UTF8Encode / UTF8ToString / UTF8Decode }
  S := 'h' + #$E9 + #$20AC;
  R := UTF8Encode(S);
  Writeln(Length(R), ' ', CP(Pointer(R)), ' ', ByteHex(R));
  T := UTF8ToString(R);
  Writeln(T = S, ' ', Length(T));
  T := UTF8Decode(R);
  Writeln(T = S, ' ', Length(T));
  S := '';
  R := UTF8Encode(S);
  T := UTF8ToString(R);
  Writeln(Length(R), ' ', R = '', ' ', Length(T));

  { code pages of RawByteString blocks and of concatenations (first non-nil operand) }
  SetLength(R, 2);
  R[1] := 'o';
  R[2] := 'k';
  A := 'ay';
  R2 := R + A;
  Writeln(R2, ' ', CP(Pointer(R)), ' ', CP(Pointer(R2)));
  B := A + R;
  Writeln(B, ' ', CP(Pointer(B)));
  R2 := E + R;
  Writeln(R2, ' ', CP(Pointer(R2)), ' ', Pointer(R2) = Pointer(R));
  R2 := E + E;
  Writeln(Length(R2), ' ', R2 = '');
  A := A + A;
  Writeln(A, ' ', RC(Pointer(A)));
  A := A + 'z';
  Writeln(A, ' ', RC(Pointer(A)), ' ', CP(Pointer(A)));

  { UnicodeString -> ShortString: truncated back to a UTF-8 sequence boundary }
  S := 'ab' + #$E9 + #$E9;
  H5 := TS5(S);
  H3 := TS3(S);
  Writeln(Length(H5), ' ', ShortHex(H5), ' | ', Length(H3), ' ', ShortHex(H3));
  S := #$D83D + #$DE00 + 'z';
  H5 := TS5(S);
  H3 := TS3(S);
  Writeln(Length(H5), ' ', ShortHex(H5), ' | ', Length(H3));
  T := H5;
  Writeln(Length(T), ' ', T = S);
  S := '';
  H5 := TS5(S);
  T := H5;
  Writeln(Length(H5), ' ', Length(T));

  { ShortString -> ShortString truncates the same way }
  S := 'a' + #$E9 + 'x';
  H := ShortString(S);
  H3 := H;
  Writeln(Length(H), ' ', ShortHex(H3));
  S := 'ab' + #$E9;
  H := ShortString(S);
  H3 := H;
  Writeln(ShortHex(H3));
  H := 'abcdefgh';
  H3 := H;
  H5 := H3;
  Writeln(H3, ' ', H5, ' ', Length(H5));

  { char conversions }
  AC := AnsiChar(233);
  C := AC;
  S := AC;
  Writeln(Ord(C), ' ', Length(S), ' ', UnitHex(S));
  A := AC;
  Writeln(Length(A), ' ', ByteHex(A), ' ', UnitHex(string(A)));
  C := #$20AC;
  A := AnsiString(C);
  Writeln(Length(A), ' ', ByteHex(A));

  { PAnsiChar -> AnsiString }
  S := 'xyz';
  A := AnsiString(S);
  P := PAnsiChar(A);
  B := P;
  Writeln(B, ' ', RC(Pointer(A)), ' ', RC(Pointer(B)), ' ', Pointer(A) <> Pointer(B));
  P := nil;
  B := P;
  Writeln(Length(B), ' ', Pointer(B) = nil);

  { SetLength, Copy }
  S := 'hello';
  A := AnsiString(S);
  B := A;
  SetLength(B, 7);
  B[6] := '!';
  B[7] := '?';
  Writeln(A, ' ', B, ' ', RC(Pointer(A)), ' ', RC(Pointer(B)));
  SetLength(B, 2);
  Writeln(B, ' ', Length(B), ' ', RC(Pointer(B)));
  SetLength(B, 0);
  Writeln(Length(B), ' ', Pointer(B) = nil);
  Writeln(Copy(A, 2, 3), ' ', Copy(A, 0, 2), ' ', Copy(A, 4, 100), ' [', Copy(A, 6, 1), '] [', Copy(A, 2, 0), ']');
  B := Copy(R, 1, 1);
  Writeln(B, ' ', CP(Pointer(B)));
  A := Copy(A, 2, MaxInt);
  Writeln(A, ' ', Length(A));
end;

begin
  Base := _LiveAllocCount;
  Run;
  Writeln('leaks ', _LiveAllocCount - Base);
end.
