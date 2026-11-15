// covers: RTL-011, RTL-012
program Sys_BasicTypes;
{ system.md section 1.1: Extended is exactly Double (same size, same RTTI kind and float type, same arithmetic);
  LongInt and LongWord are 32-bit on this target. }
uses TestKit, TypInfo;

var
  E: Extended;
  D: Double;
  L: LongInt;
  W: LongWord;

begin
  Section('Extended = Double');
  CheckEqI(8, SizeOf(Extended), 'SizeOf(Extended)');
  CheckEqI(SizeOf(Double), SizeOf(E), 'Extended variable as large as Double');
  Check(PTypeInfo(TypeInfo(Extended))^.Kind = tkFloat, 'Extended RTTI kind is tkFloat');
  Check(GetTypeData(PTypeInfo(TypeInfo(Extended)))^.FloatType = GetTypeData(PTypeInfo(TypeInfo(Double)))^.FloatType,
    'Extended RTTI float type equals that of Double');
  Check(GetTypeData(PTypeInfo(TypeInfo(Extended)))^.FloatType = ftDouble, 'Extended RTTI float type is ftDouble');
  E := 1.0;
  E := E / 3.0;
  D := 1.0;
  D := D / 3.0;
  CheckEqF(D, E, 0, 'Extended arithmetic is Double arithmetic');
  E := 1.0e308;
  E := E * 10.0;
  D := 1.0e308;
  D := D * 10.0;
  CheckEqF(D, E, 0, 'Extended overflows where Double does');

  Section('LongInt and LongWord are 32-bit');
  CheckEqI(4, SizeOf(LongInt), 'SizeOf(LongInt)');
  CheckEqI(4, SizeOf(LongWord), 'SizeOf(LongWord)');
  CheckEqI(-2147483647 - 1, Low(LongInt), 'Low(LongInt)');
  CheckEqI(2147483647, High(LongInt), 'High(LongInt)');
  CheckEqI(0, Low(LongWord), 'Low(LongWord)');
  CheckEqI(4294967295, High(LongWord), 'High(LongWord)');
  {$Q-}
  {$R-}
  L := High(LongInt);
  L := L + 1;
  CheckEqI(Low(LongInt), L, 'LongInt wraps at 32 bits');
  W := High(LongWord);
  W := W + 1;
  CheckEqI(0, W, 'LongWord wraps at 32 bits');
  Done;
end.
