// covers: RTL-090, RTL-092
program Sys_WriteInt;
{ system.md section 10.2: every integer type at its extremes, Boolean and the C-style booleans, Char, AnsiChar. }
var
  I8: ShortInt;
  U8: Byte;
  I16: SmallInt;
  U16: Word;
  I32: Integer;
  U32: Cardinal;
  I64: Int64;
  U64: UInt64;
  NI: NativeInt;
  NU: NativeUInt;
  BB: ByteBool;
  LB: LongBool;
  C: Char;
  A: AnsiChar;
begin
  I8 := -128;
  U8 := 255;
  Writeln(I8, ' ', U8, ' ', High(ShortInt), ' ', Low(Byte));
  I16 := -32768;
  U16 := 65535;
  Writeln(I16, ' ', U16, ' ', High(SmallInt));
  I32 := Low(Integer);
  U32 := High(Cardinal);
  Writeln(I32, ' ', U32, ' ', High(Integer));
  I64 := Low(Int64);
  U64 := High(UInt64);
  Writeln(I64, ' ', U64, ' ', High(Int64));
  NI := -1;
  NU := High(NativeUInt);
  Writeln(NI, ' ', NU, ' ', 0, ' ', -0, ' ', 7, ' ', -10);
  BB := False;
  LB := True;
  Writeln(True, ' ', False, ' ', BB, ' ', LB, ' ', 1 < 2);
  C := 'Z';
  A := 'a';
  Writeln(C, A, '!', #65);
  Write('no line end');
  Writeln;
  Write(1, 2);
  Write(3);
  Writeln;
  Writeln(ErrOutput, 'to stderr ', 42);
end.
