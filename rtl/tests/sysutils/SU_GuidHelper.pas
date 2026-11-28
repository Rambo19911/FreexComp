// phase: 8
// covers: RTL-1275, RTL-1280
program SU_GuidHelper;
{ sysutils.md section 12 and 7.2, task R-270 (F10-030): TGuidHelper and CreateGUID (block F of standard-helpers.md
  section 5.2). Create from text (StringToGUID), from 16 bytes in memory order (D1, D2, D3 least significant byte
  first, then D4) and from eleven parts; Empty / IsEmpty; ToByteArray and the round trips; ToString; the errors with
  their exact class and message; NewGuid and CreateGUID ask the operating system (CoCreateGuid of ole32.dll, found at
  run time) in a program that never initialises COM. }
uses
  SysUtils, TestKit;

function BytesText(const B: TBytes): string;
var
  I: Integer;
begin
  Result := '';
  for I := 0 to High(B) do
  begin
    if I > 0 then
      Result := Result + ' ';
    Result := Result + IntToHex(B[I], 2);
  end;
end;

function BytesOfLength(N: Integer): TBytes;
var
  I: Integer;
begin
  SetLength(Result, N);
  for I := 0 to N - 1 do
    Result[I] := I;
end;

procedure ArrayFails(const Name: string; const B: TBytes);
var
  G: TGUID;
begin
  Expect('EArgumentException', 'Byte array for GUID must be exactly 16 bytes long');
  try
    G := TGUID.Create(B);
    Writeln('# no exception, ', G.ToString);
  except
    on E: Exception do CheckRaises(Name, E.ClassName, E.Message);
  end;
end;

procedure TestCreate;
const
  Text = '{00112233-4455-6677-8899-AABBCCDDEEFF}';
var
  G, H, Z: TGUID;
  B: TBytes;
begin
  Section('Create, ToString, ToByteArray');
  G := TGUID.Create(Text);
  CheckEqS(Text, G.ToString, 'G.ToString');
  Check(G.D1 = $00112233, 'G.D1');
  Check((G.D2 = $4455) and (G.D3 = $6677), 'G.D2, G.D3');
  Check((G.D4[0] = $88) and (G.D4[7] = $FF), 'G.D4');
  B := G.ToByteArray;
  CheckEqI(16, Length(B), 'ToByteArray length');
  CheckEqS('33 22 11 00 55 44 77 66 88 99 AA BB CC DD EE FF', BytesText(B), 'ToByteArray in memory order');
  H := TGUID.Create(G.ToByteArray);
  Check(H = G, 'TGUID.Create(G.ToByteArray) = G');
  H := TGUID.Create($00112233, $4455, $6677, $88, $99, $AA, $BB, $CC, $DD, $EE, $FF);
  Check(H = G, 'TGUID.Create from eleven parts = G');
  CheckEqS(Text, H.ToString, 'eleven parts: ToString');
  H := TGUID.Create('{00112233-4455-6677-8899-aabbccddeeff}');
  Check(H = G, 'Create accepts lower-case digits');
  CheckEqS(Text, H.ToString, 'ToString writes upper-case digits');
  H := TGUID.Create(BytesOfLength(16));
  CheckEqS('{03020100-0504-0706-0809-0A0B0C0D0E0F}', H.ToString, 'Create(B) of 0 .. 15');
  CheckEqS(GUIDToString(G), G.ToString, 'G.ToString = GUIDToString(G)');
  Section('Empty and IsEmpty');
  Z := TGUID.Empty;
  CheckEqS('{00000000-0000-0000-0000-000000000000}', Z.ToString, 'Z.ToString');
  Check(Z.IsEmpty, 'Z.IsEmpty');
  Check(not G.IsEmpty, 'not G.IsEmpty');
  Check(TGUID.Create(Z.ToByteArray).IsEmpty, 'the bytes of Empty give Empty');
  Check(TGUID.Create(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1) <> Z, 'one bit set is not empty');
  Check(not TGUID.Create(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1).IsEmpty, 'one bit set: IsEmpty is False');
end;

procedure TestErrors;
var
  G: TGUID;
  Nothing: TBytes;
begin
  Section('errors');
  Expect('EConvertError', '''nonsense'' is not a valid GUID value');
  try
    G := TGUID.Create('nonsense');
    Writeln('# no exception, ', G.ToString);
  except
    on E: Exception do CheckRaises('TGUID.Create(''nonsense'')', E.ClassName, E.Message);
  end;
  ArrayFails('15 bytes', BytesOfLength(15));
  ArrayFails('17 bytes', BytesOfLength(17));
  Nothing := nil;
  ArrayFails('nil', Nothing);
end;

procedure TestNew;
var
  N1, N2, C: TGUID;
begin
  Section('NewGuid and CreateGUID');
  N1 := TGUID.NewGuid;
  N2 := TGUID.NewGuid;
  Check(not N1.IsEmpty, 'N1 is not empty');
  Check(not N2.IsEmpty, 'N2 is not empty');
  Check(N1 <> N2, 'two new GUIDs differ');
  CheckEqI(38, Length(N1.ToString), 'text length 38');
  Check(StringToGUID(N1.ToString) = N1, 'the text of a new GUID reads back');
  CheckEqI(0, CreateGUID(C), 'CreateGUID(C) = 0');
  Check(not C.IsEmpty and (C <> N1) and (C <> N2), 'CreateGUID gives a third value');
end;

begin
  TestCreate;
  TestErrors;
  TestNew;
  Done;
end.
