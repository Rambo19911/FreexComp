// covers: LANG-670, LANG-672, ABI-150, ABI-151, CMP-415
program FormEmbed;

{ The bytes of UMain.form are registered by UMain's initialization under the upper-cased root class name and found with
  FindResourceData (ASCII case-insensitive); an unknown name gives False, nil and 0. }

{$POINTERMATH ON}

uses UMain;

var
  D, D2: Pointer;
  S, S2: NativeInt;
  I: Integer;
  First: string;
begin
  Writeln('found ', FindResourceData('TMainForm', D, S), ' size ', S);
  First := '';
  for I := 0 to 5 do
    First := First + Chr(PByte(D)[I]);
  Writeln('first ', First, ' last ', PByte(D)[S - 1]);
  Writeln('lower ', FindResourceData('tmainform', D2, S2), ' same ', D2 = D, ' ', S2 = S);
  Writeln('missing ', FindResourceData('TOther', D2, S2), ' nil ', D2 = nil, ' size ', S2);
end.