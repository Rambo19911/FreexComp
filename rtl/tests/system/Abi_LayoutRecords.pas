// covers: RTL-029, RTL-127, ABI-073
program Abi_LayoutRecords;
{ Sizes and field offsets of System's private layout records (the same declarations, included from rtl/src)
  against the numbers of rtl-abi.md section 2, 3.1, 4, 7.2, 10 and system.md RTL-127; TVmtHeader also read on the
  compiler's VMT of TObject and of a class with a managed field. }
{$POINTERMATH ON}
{$I ..\..\src\AbiLayout.inc}

var
  S: TStrRec;
  D: TDynArrayRec;
  T: TTextRec;
  DI: TDynArrayTypeInfo;
  RI: TRecordTypeInfo;
  MF: TManagedField;
  IE: TIntfEntry;
  AI: TArrayTypeInfo;
  V: TVmtHeader;
  H: PVmtHeader;
  VR: TVarRec;

type
  TWithField = class
    S: string;
  end;

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
  Writeln('TIntfEntry ', SizeOf(TIntfEntry), ': ', Offset(@IE, @IE.Guid), ' ', Offset(@IE, @IE.Imt), ' ',
    Offset(@IE, @IE.Offset));
  Writeln('TArrayTypeInfo ', SizeOf(TArrayTypeInfo), ': ', Offset(@AI, @AI.Kind), ' ', Offset(@AI, @AI.Name), ' ',
    Offset(@AI, @AI.Size), ' ', Offset(@AI, @AI.ElCount), ' ', Offset(@AI, @AI.ElType));
  Writeln('TVmtHeader ', SizeOf(TVmtHeader), ': ', Offset(@V, @V.Parent), ' ', Offset(@V, @V.InstanceSize), ' ',
    Offset(@V, @V.ClassName), ' ', Offset(@V, @V.TypeInfo), ' ', Offset(@V, @V.IntfTable), ' ', Offset(@V, @V.MsgTable), ' ',
    Offset(@V, @V.InitTable), ' ', Offset(@V, @V.Reserved), ' slots ', VmtFirstSlot);
  H := PVmtHeader(TObject);
  Writeln('TObject VMT: parent nil ', H^.Parent = nil, ', size ', H^.InstanceSize, ', init table nil ', H^.InitTable = nil);
  H := PVmtHeader(TWithField);
  Writeln('TWithField VMT: parent TObject ', H^.Parent = Pointer(TObject), ', size ', H^.InstanceSize, ', init count ',
    PNativeInt(H^.InitTable)^, ', field offset ', PManagedField(PByte(H^.InitTable) + 8)^.Offset);
  { System's public TVarRec (rtl-abi.md section 9.10, RTL-013): VType at 0, every payload field at 8 }
  Writeln('TVarRec ', SizeOf(TVarRec), ': ', Offset(@VR, @VR.VType), ' ', Offset(@VR, @VR.VInteger), ' ',
    Offset(@VR, @VR.VBoolean), ' ', Offset(@VR, @VR.VChar), ' ', Offset(@VR, @VR.VExtended), ' ',
    Offset(@VR, @VR.VString), ' ', Offset(@VR, @VR.VPointer), ' ', Offset(@VR, @VR.VPChar), ' ',
    Offset(@VR, @VR.VObject), ' ', Offset(@VR, @VR.VClass), ' ', Offset(@VR, @VR.VWideChar), ' ',
    Offset(@VR, @VR.VPWideChar), ' ', Offset(@VR, @VR.VAnsiString), ' ', Offset(@VR, @VR.VInterface), ' ',
    Offset(@VR, @VR.VInt64), ' ', Offset(@VR, @VR.VUnicodeString));
  Writeln('vt: ', vtInteger, ' ', vtBoolean, ' ', vtChar, ' ', vtExtended, ' ', vtString, ' ', vtPointer, ' ',
    vtPChar, ' ', vtObject, ' ', vtClass, ' ', vtWideChar, ' ', vtPWideChar, ' ', vtAnsiString, ' ', vtCurrency, ' ',
    vtVariant, ' ', vtInterface, ' ', vtWideString, ' ', vtInt64, ' ', vtUnicodeString);
end.
