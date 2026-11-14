// covers: RTL-078, RTL-091
program Sys_WriteStr;
{ Write of strings (system.md section 10.2): string values of every origin (literal, heap, empty, result) with field
  widths counted in UTF-16 code units; PChar (nil writes only the padding), ShortString and AnsiString (UTF-8 text,
  invalid bytes become U+FFFD). TODO(C-063): Write(P), Write(ShortString), Write(AnsiString) are not lowered by the
  compiler yet, so their helpers are called directly on hand-built values. }
type
  TAnsiBlock = record
    RefCount, Len: Integer;
    CodePage, ElemSize: Word;
    Reserved: Integer;
    Data: array[0..15] of Byte;
  end;
  TShort = array[0..255] of Byte;

var
  Base: NativeInt;

function Twice(const S: string): string;
begin
  Result := S + S;
end;

{ Count bytes as an AnsiString value in X; the result is the value (nil for 0 bytes) }
function Ansi(var X: TAnsiBlock; B0, B1, B2, B3, B4, B5: Integer; Count: Integer): Pointer;
begin
  X.RefCount := -1;
  X.Len := Count;
  X.CodePage := 65001;
  X.ElemSize := 1;
  X.Reserved := 0;
  X.Data[0] := B0;
  X.Data[1] := B1;
  X.Data[2] := B2;
  X.Data[3] := B3;
  X.Data[4] := B4;
  X.Data[5] := B5;
  X.Data[Count] := 0;
  Result := nil;
  if Count > 0 then
    Result := @X.Data[0];
end;

procedure Run;
var
  S, E: string;
  C: Char;
  Buf: array[0..7] of WideChar;
  X: TAnsiBlock;
  Sh: TShort;
begin
  S := 'heap';
  S := S + '!';
  Writeln('[', S, '] [', S:8, '] [', S:2, '] [', S:-3, ']');
  Writeln('[', E, '] [', E:3, ']');
  Writeln('[', 'lit':5, '] [', Twice('ab'), '] [', Twice('cd'):6, ']');
  C := #$D83D;
  S := C;
  C := #$DE00;
  S := S + C + 'ä';
  Writeln('[', S, '] [', S:5, ']');

  Buf[0] := 'w';
  Buf[1] := 'i';
  Buf[2] := #$E9;
  Buf[3] := #0;
  Write('[');
  _WritePWChar(Output, @Buf[0], 0);
  Write('] [');
  _WritePWChar(Output, @Buf[0], 6);
  Write('] [');
  _WritePWChar(Output, nil, 0);
  Write('] [');
  _WritePWChar(Output, nil, 2);
  Write('] [');
  Buf[0] := #0;
  _WritePWChar(Output, @Buf[0], 1);
  Writeln(']');

  Write('[');
  _WriteAStr(Output, Ansi(X, $61, $C3, $A9, 0, 0, 0, 3), 0);
  Write('] [');
  _WriteAStr(Output, Ansi(X, $61, $C3, $A9, 0, 0, 0, 3), 4);
  Write('] [');
  _WriteAStr(Output, Ansi(X, $F0, $9F, $98, $80, 0, 0, 4), 3);
  Write('] [');
  _WriteAStr(Output, nil, 2);
  Write('] [');
  _WriteAStr(Output, Ansi(X, $61, $FF, $C3, $41, $80, $62, 6), 0);
  Write('] [');
  _WriteAStr(Output, Ansi(X, $E0, $80, $80, $ED, $A0, $80, 6), 0);
  Write('] [');
  _WriteAStr(Output, Ansi(X, $E2, $82, 0, 0, 0, 0, 2), 0);
  Writeln(']');

  Sh[0] := 0;
  Write('[');
  _WriteSStr(Output, @Sh, 2);
  Write('] [');
  Sh[0] := 4;
  Sh[1] := $E2;
  Sh[2] := $82;
  Sh[3] := $AC;
  Sh[4] := $21;
  _WriteSStr(Output, @Sh, 0);
  Write('] [');
  _WriteSStr(Output, @Sh, 4);
  Writeln(']');
end;

begin
  Base := _LiveAllocCount;
  Run;
  Writeln('all released ', _LiveAllocCount - Base);
end.
