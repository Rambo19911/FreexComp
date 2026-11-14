// covers: LANG-200, LANG-213, LANG-920
program IntegerTypesRanges;
begin
  Writeln('ShortInt ', Low(ShortInt), ' ', High(ShortInt));
  Writeln('SmallInt ', Low(SmallInt), ' ', High(SmallInt));
  Writeln('Integer ', Low(Integer), ' ', High(Integer));
  Writeln('LongInt ', Low(LongInt), ' ', High(LongInt));
  Writeln('Int64 ', Low(Int64), ' ', High(Int64));
  Writeln('NativeInt ', Low(NativeInt), ' ', High(NativeInt));
  Writeln('Byte ', Low(Byte), ' ', High(Byte));
  Writeln('Word ', Low(Word), ' ', High(Word));
  Writeln('Cardinal ', Low(Cardinal), ' ', High(Cardinal));
  Writeln('LongWord ', Low(LongWord), ' ', High(LongWord));
  Writeln('UInt64 ', Low(UInt64), ' ', High(UInt64));
  Writeln('NativeUInt ', Low(NativeUInt), ' ', High(NativeUInt));
  Writeln('sizes ', SizeOf(ShortInt), SizeOf(SmallInt), SizeOf(Integer), SizeOf(Int64), SizeOf(NativeInt),
    SizeOf(Byte), SizeOf(Word), SizeOf(Cardinal), SizeOf(UInt64), SizeOf(NativeUInt));
  Writeln('MaxInt ', MaxInt, ' MaxLongInt ', MaxLongInt);
end.
