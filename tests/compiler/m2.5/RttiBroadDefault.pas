// covers: LANG-648, LANG-882, ABI-143, ABI-144, CMP-408
program RttiBroadDefault;

{ Default (broad) RTTI scope: the class RTTI tables list every field (a private one with visibility 0) and the public
  and published methods; a class under the PUBLISHED directive lists only its published members, one under the BROAD
  directive everything. MethodAddress considers only published methods whatever the scope. }

{$POINTERMATH ON}

type
  TDefault = class
  private
    FSecret: Integer;
  public
    procedure Open;
  published
    FLink: TObject;
    procedure Shown;
  end;

{$RTTI PUBLISHED}
  TNarrow = class
  private
    FSecret: Integer;
  public
    procedure Open;
  published
    FLink: TObject;
    procedure Shown;
  end;

{$RTTI BROAD}
  TWide = class
  private
    FSecret: Integer;
  public
    procedure Open;
  published
    FLink: TObject;
    procedure Shown;
  end;
{$RTTI DEFAULT}

procedure TDefault.Open; begin end;
procedure TDefault.Shown; begin end;
procedure TNarrow.Open; begin end;
procedure TNarrow.Shown; begin end;
procedure TWide.Open; begin end;
procedure TWide.Shown; begin end;

function At(P: Pointer; Offset: Integer): Pointer;
begin
  Result := PPointer(PByte(P) + Offset)^;
end;

procedure Dump(TI: PByte);
var
  I: Integer;
  E: PByte;
begin
  Writeln(PChar(At(TI, 8)), ': fields ', PNativeInt(TI + 72)^, ', methods ', PNativeInt(TI + 56)^);
  for I := 0 to PNativeInt(TI + 72)^ - 1 do
  begin
    E := PByte(At(TI, 80)) + I * 32;
    Writeln('  field ', PChar(At(E, 0)), ' offset ', PNativeInt(E + 8)^, ' visibility ', E[24], ' type ', PChar(At(At(E, 16), 8)));
  end;
  for I := 0 to PNativeInt(TI + 56)^ - 1 do
  begin
    E := PByte(At(TI, 64)) + I * 24;
    Writeln('  method ', PChar(At(E, 0)), ' visibility ', E[16], ' kind ', E[17]);
  end;
end;

begin
  Dump(TypeInfo(TDefault));
  Dump(TypeInfo(TNarrow));
  Dump(TypeInfo(TWide));
  Writeln('MethodAddress Open ', TDefault.MethodAddress('Open') <> nil, ' Shown ', TDefault.MethodAddress('Shown') <> nil,
    ' wide Open ', TWide.MethodAddress('Open') <> nil);
end.