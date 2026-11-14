// covers: RTL-079
program Sys_StrInt;
{ Str writes exactly the text Write would write (LANG-951): integers of every width with their extremes and field
  widths, reals in the scientific form (default width 23) and the fixed form, the special values; the previous value
  of the target is released. Each line shows Str's result next to Write's text for the same argument. }
var
  Base: NativeInt;

procedure Run;
var
  S: string;
  I8: ShortInt;
  U8: Byte;
  I16: SmallInt;
  U16: Word;
  I32: Integer;
  U32: Cardinal;
  I64: Int64;
  U64: UInt64;
  D, Zero: Double;
  F: Single;
begin
  I8 := -128;
  Str(I8, S);
  Writeln('[', S, '] [', I8, ']');
  U8 := 255;
  Str(U8, S);
  Writeln('[', S, '] [', U8, ']');
  I16 := -32768;
  Str(I16:8, S);
  Writeln('[', S, '] [', I16:8, ']');
  U16 := 65535;
  Str(U16:3, S);
  Writeln('[', S, '] [', U16:3, ']');
  I32 := Low(Integer);
  Str(I32, S);
  Writeln('[', S, '] [', I32, ']');
  U32 := High(Cardinal);
  Str(U32:12, S);
  Writeln('[', S, '] [', U32:12, ']');
  I64 := Low(Int64);
  Str(I64, S);
  Writeln('[', S, '] [', I64, ']');
  I64 := High(Int64);
  Str(I64:25, S);
  Writeln('[', S, '] [', I64:25, ']');
  U64 := High(UInt64);
  Str(U64, S);
  Writeln('[', S, '] [', U64, ']');
  Str(0, S);
  Writeln('[', S, '] [', 0, ']');
  Str(-5:-3, S);
  Writeln('[', S, '] [', -5:-3, ']');
  Str(7:1, S);
  Writeln('[', S, '] [', 7:1, '] ', Length(S), ' ', StringRefCount(S));

  D := 1.5;
  Str(D, S);
  Writeln('[', S, '] [', D, ']');
  Str(D:10, S);
  Writeln('[', S, '] [', D:10, ']');
  Str(-1234.5:12, S);
  Writeln('[', S, '] [', -1234.5:12, ']');
  Str(0.0, S);
  Writeln('[', S, '] [', 0.0, ']');
  Str(3.14159:8:2, S);
  Writeln('[', S, '] [', 3.14159:8:2, ']');
  Str(2.5:0:0, S);
  Writeln('[', S, '] [', 2.5:0:0, ']');
  Str(-0.005:0:2, S);
  Writeln('[', S, '] [', -0.005:0:2, ']');
  Str(1e20:0:1, S);
  Writeln('[', S, '] [', 1e20:0:1, ']');
  F := 0.1;
  Str(F:0:10, S);
  Writeln('[', S, '] [', F:0:10, ']');
  Zero := 0.0;
  D := 1 / Zero;
  Str(D, S);
  Writeln('[', S, '] [', D, ']');
  Str(-D:6, S);
  Writeln('[', S, '] [', -D:6, ']');
  D := Zero / Zero;
  Str(D:5:1, S);
  Writeln('[', S, '] [', D:5:1, ']');
end;

begin
  Base := _LiveAllocCount;
  Run;
  Writeln('all released ', _LiveAllocCount - Base);
end.
