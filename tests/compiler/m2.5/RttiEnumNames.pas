// covers: CMP-405, CMP-407, ABI-140, LANG-210
program RttiEnumNames;

{ TypeInfo(T) of enumerations, an enumeration subrange, Boolean, a set and several built-in types, read with the
  rtl-abi.md section 10 offsets: Kind at 0, Name at 8; ordinal kinds: OrdType at 16, Min at 24, Max at 32;
  enumerations: BaseType at 40, Names at 48 (Max - Min + 1 name pointers); tkFloat: FloatType at 16; tkSet: Size
  at 16, CompType at 24. }

{$POINTERMATH ON}

type
  TColor = (Red, Green, Blue);
  TWarm = Green..Blue;
  TColors = set of TColor;

function At(P: Pointer; Offset: Integer): Pointer;
begin
  Result := PPointer(PByte(P) + Offset)^;
end;

function Str(P: Pointer): string;
begin
  if P = nil then
    Result := ''
  else
    Result := PChar(P);
end;

procedure Ordinal(const Title: string; TI: Pointer);
var
  Min, Max, I: Int64;
  Names: PPointer;
begin
  Min := PInt64(PByte(TI) + 24)^;
  Max := PInt64(PByte(TI) + 32)^;
  Write(Title, ': kind ', (PByte(TI))^, ' name ', Str(At(TI, 8)), ' ord ', (PByte(TI) + 16)^, ' ', Min, '..', Max);
  if (PByte(TI))^ = 3 then
  begin
    Write(' base ', Str(At(At(TI, 40), 8)), ' names');
    Names := At(TI, 48);
    for I := 0 to Max - Min do
      Write(' ', Str(Names[I]));
  end;
  Writeln;
end;

var
  TI: Pointer;
begin
  Ordinal('TColor', TypeInfo(TColor));
  Ordinal('TWarm', TypeInfo(TWarm));
  Ordinal('Boolean', TypeInfo(Boolean));
  Ordinal('Integer', TypeInfo(Integer));
  Ordinal('Byte', TypeInfo(Byte));
  Ordinal('Char', TypeInfo(Char));
  Ordinal('Int64', TypeInfo(Int64));
  Writeln('self ', At(TypeInfo(TColor), 40) = TypeInfo(TColor), ' same ', TypeInfo(TColor) = TypeInfo(TColor));
  TI := TypeInfo(TColors);
  Writeln('TColors: kind ', (PByte(TI))^, ' name ', Str(At(TI, 8)), ' size ', PInteger(PByte(TI) + 16)^,
    ' comp ', Str(At(At(TI, 24), 8)));
  TI := TypeInfo(Double);
  Writeln('Double: kind ', (PByte(TI))^, ' name ', Str(At(TI, 8)), ' float ', (PByte(TI) + 16)^);
  TI := TypeInfo(string);
  Writeln('string: kind ', (PByte(TI))^, ' name ', Str(At(TI, 8)));
end.
