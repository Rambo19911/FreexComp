// covers: RTL-029, RTL-127
program Abi_LayoutRecords;
{ Sizes and field offsets of System's private layout records (the same declarations, included from rtl/src)
  against the numbers of rtl-abi.md section 2, 3.1, 4 and system.md RTL-127. }
{$I ..\..\src\AbiLayout.inc}

var
  S: TStrRec;
  D: TDynArrayRec;
  T: TTextRec;

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
end.
