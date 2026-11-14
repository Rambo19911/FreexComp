// covers: RTL-029, RTL-127
program Abi_LayoutRecords;
{ Sizes and field offsets of System's private layout records (the same declarations, included from rtl/src)
  against the numbers of rtl-abi.md section 2, 3.1, 4, 10 and system.md RTL-127. }
{$I ..\..\src\AbiLayout.inc}

var
  S: TStrRec;
  D: TDynArrayRec;
  T: TTextRec;
  DI: TDynArrayTypeInfo;
  RI: TRecordTypeInfo;
  MF: TManagedField;
  AI: TArrayTypeInfo;

function Offset(Base, Field: Pointer): NativeInt;
begin
  Result := NativeInt(Field) - NativeInt(Base);
end;

begin
  Writeln('Text ', SizeOf(Text), ' TTextRec ', SizeOf(TTextRec));
  Writeln('TStrRec ', SizeOf(TStrRec), ': ', Offset(@S, @S.RefCount), ' ', Offset(@S, @S.Length), ' ',
    Offset(@S, @S.CodePage), ' ', Offset(@S, @S.ElemSize), ' ', Offset(@S, @S.Reserved));
  Writeln('TDynArrayRec ', SizeOf(TDynArrayRec), ': ', Offset(@D, @D.RefCount), ' ', Offset(@D, @D.Reserved), ' ',
    Offset(@D, @D.Length));
  Writeln('TTextRec: ', Offset(@T, @T.Handle), ' ', Offset(@T, @T.Buffer), ' ', Offset(@T, @T.Mode), ' ',
    Offset(@T, @T.Kind), ' ', Offset(@T, @T.CodePage), ' ', Offset(@T, @T.BufPos), ' ', Offset(@T, @T.BufEnd), ' ',
    Offset(@T, @T.PendingHigh), ' ', Offset(@T, @T.EofSeen), ' ', Offset(@T, @T.Name), ' ', SizeOf(T.Name));
  Writeln('TTextRec input: ', Offset(@T, @T.Magic), ' ', Offset(@T, @T.Look), ' ', Offset(@T, @T.LookCount), ' ',
    Offset(@T, @T.StartDone), ' ', Offset(@T, @T.Std), ' ', Offset(@T, @T.LineStart), ' ', Offset(@T, @T.NameTooLong), ' ',
    Offset(@T, @T.Padding), ' ', SizeOf(T.Padding));
  Writeln('TDynArrayTypeInfo ', SizeOf(TDynArrayTypeInfo), ': ', Offset(@DI, @DI.Kind), ' ', Offset(@DI, @DI.Name), ' ',
    Offset(@DI, @DI.ElSize), ' ', Offset(@DI, @DI.ElType), ' ', Offset(@DI, @DI.ManagedElType));
  Writeln('TRecordTypeInfo ', SizeOf(TRecordTypeInfo), ': ', Offset(@RI, @RI.Kind), ' ', Offset(@RI, @RI.Name), ' ',
    Offset(@RI, @RI.Size), ' ', Offset(@RI, @RI.ManagedCount), ' ', Offset(@RI, @RI.ManagedFields));
  Writeln('TManagedField ', SizeOf(TManagedField), ': ', Offset(@MF, @MF.TypeInfo), ' ', Offset(@MF, @MF.Offset));
  Writeln('TArrayTypeInfo ', SizeOf(TArrayTypeInfo), ': ', Offset(@AI, @AI.Kind), ' ', Offset(@AI, @AI.Name), ' ',
    Offset(@AI, @AI.Size), ' ', Offset(@AI, @AI.ElCount), ' ', Offset(@AI, @AI.ElType));
end.
