// covers: LANG-340
program ConstPointerCast;

const
  IDC_ARROW = PWideChar(32512);
  IDI_APPLICATION = PChar(32512 + 0);
  Nothing = Pointer(0);
  Marker = Pointer($10);
  AllOnes = PAnsiChar(-1);

type
  TTable = array[0..1] of PChar;

const
  Table: TTable = (PChar(1), PChar(2));
  Typed: Pointer = Pointer(255);

procedure Show(P: Pointer);
begin
  Writeln(NativeUInt(P));
end;

var
  P: PChar;
begin
  P := IDC_ARROW;
  Writeln(NativeUInt(P));
  Show(IDC_ARROW);
  Writeln(Nothing = nil);
  Show(Marker);
  Show(AllOnes);
  Show(Table[1]);
  Show(Typed);
  Writeln(P = IDI_APPLICATION);
end.
