// covers: UIF-930, UIF-931, UIF-932, UIF-933
program Menu_ShortCutText;
{ U-072, actions-menus.md section 3: the TShortCut encoding (virtual key in the low byte plus scShift/scCtrl/scAlt;
  a key above 255 gives 0) and its inverse; ShortCutToText with the fixed modifier order Ctrl+, Alt+, Shift+ and
  every fixed key name of UIF-932; '' for 0 and for unnamed keys; other keys named by the system (only checked to
  be non-empty, the text depends on the keyboard layout); TextToShortCut case-insensitive with optional spaces
  around '+' and modifiers in any order, recognising exactly the fixed names; round trip over every fixed name and
  every modifier combination. }
uses
  Windows, SysUtils, Classes, Menus, TestKit;

function Txt(Key: Word; Shift: TShiftState): string;
begin
  Result := ShortCutToText(ShortCut(Key, Shift));
end;

procedure Run;
var
  Key, K: Word;
  Shift: TShiftState;
  Names, Failures: string;
  SC: TShortCut;
  M: Integer;
  Ok: Boolean;
begin
  Section('encoding (UIF-930)');
  CheckEqI(scShift, $2000, 'scShift');
  CheckEqI(scCtrl, $4000, 'scCtrl');
  CheckEqI(scAlt, $8000, 'scAlt');
  CheckEqI(scNone, 0, 'scNone');
  CheckEqI(32883, ShortCut(VK_F4, [ssAlt]), 'Alt+F4 = 32883');
  CheckEqI(16463, ShortCut(Ord('O'), [ssCtrl]), 'Ctrl+O = 16463');
  CheckEqI($E041, ShortCut(Ord('A'), [ssShift, ssCtrl, ssAlt]), 'all three modifiers');
  CheckEqI($41, ShortCut(Ord('A'), [ssLeft, ssDouble]), 'mouse states are ignored');
  CheckEqI(0, ShortCut(256, [ssCtrl]), 'key above 255 gives 0');
  ShortCutToKey(32883, Key, Shift);
  CheckEqI(VK_F4, Key, 'ShortCutToKey: key');
  CheckEqB(True, Shift = [ssAlt], 'ShortCutToKey: shift');
  ShortCutToKey($E041, Key, Shift);
  CheckEqB(True, (Key = $41) and (Shift = [ssShift, ssCtrl, ssAlt]), 'ShortCutToKey: all modifiers');
  ShortCutToKey(0, Key, Shift);
  CheckEqB(True, (Key = 0) and (Shift = []), 'ShortCutToKey: 0');
  CheckEqB(True, SizeOf(TShortCut) = SizeOf(Classes.TShortCut), 'TShortCut is the Classes type');

  Section('text (UIF-931)');
  CheckEqS('', ShortCutToText(0), '0 gives empty');
  CheckEqS('Ctrl+O', ShortCutToText(16463), 'Ctrl+O');
  CheckEqS('Alt+F4', ShortCutToText(32883), 'Alt+F4');
  CheckEqS('Ctrl+Alt+Shift+A', Txt(Ord('A'), [ssShift, ssAlt, ssCtrl]), 'fixed order Ctrl, Alt, Shift');
  CheckEqS('Alt+Shift+Del', Txt(VK_DELETE, [ssShift, ssAlt]), 'Alt before Shift');
  CheckEqS('Ctrl+Shift+F12', Txt(VK_F12, [ssShift, ssCtrl]), 'Ctrl before Shift');
  CheckEqS('Shift+', Copy(Txt(Ord('Z'), [ssShift]), 1, 6), 'Shift prefix');
  CheckEqS('', ShortCutToText(scCtrl), 'modifier only, key 0: unnamed');
  CheckEqS('', ShortCutToText(scCtrl or $07), 'unnamed key 7: empty even with a modifier');

  Section('fixed key names (UIF-932)');
  Names := '';
  for K := Ord('A') to Ord('Z') do
    Names := Names + Txt(K, []);
  CheckEqS('ABCDEFGHIJKLMNOPQRSTUVWXYZ', Names, 'A to Z');
  Names := '';
  for K := Ord('0') to Ord('9') do
    Names := Names + Txt(K, []);
  CheckEqS('0123456789', Names, '0 to 9');
  Names := '';
  for K := $70 to $87 do
    Names := Names + Txt(K, []) + ' ';
  CheckEqS('F1 F2 F3 F4 F5 F6 F7 F8 F9 F10 F11 F12 F13 F14 F15 F16 F17 F18 F19 F20 F21 F22 F23 F24 ', Names,
    'F1 to F24');
  CheckEqS('BkSp,Tab,Enter,Esc,Space,PgUp,PgDn,End,Home,Left,Up,Right,Down,Ins,Del',
    Txt(8, []) + ',' + Txt(9, []) + ',' + Txt(13, []) + ',' + Txt(27, []) + ',' + Txt(32, []) + ',' +
    Txt(33, []) + ',' + Txt(34, []) + ',' + Txt(35, []) + ',' + Txt(36, []) + ',' + Txt(37, []) + ',' +
    Txt(38, []) + ',' + Txt(39, []) + ',' + Txt(40, []) + ',' + Txt(45, []) + ',' + Txt(46, []),
    'editing and navigation keys');
  Names := '';
  for K := $60 to $69 do
    Names := Names + Txt(K, []) + ',';
  CheckEqS('Num 0,Num 1,Num 2,Num 3,Num 4,Num 5,Num 6,Num 7,Num 8,Num 9,', Names, 'Num 0 to Num 9');
  CheckEqS('Num *,Num +,Num -,Num .,Num /',
    Txt($6A, []) + ',' + Txt($6B, []) + ',' + Txt($6D, []) + ',' + Txt($6E, []) + ',' + Txt($6F, []),
    'numeric-pad operators');
  CheckEqS('Ctrl+Num +', Txt($6B, [ssCtrl]), 'modifier with Num +');
  Check(Txt(VK_OEM_1, []) <> '', 'other keys: system key name, non-empty');
  Check(Txt(VK_OEM_1, []) <> 'Num +', 'other keys: not a fixed name');

  Section('parsing (UIF-933)');
  CheckEqI(16463, TextToShortCut('Ctrl+O'), 'Ctrl+O');
  CheckEqI(32883, TextToShortCut('Alt+F4'), 'Alt+F4');
  CheckEqI(16463, TextToShortCut('ctrl+o'), 'lower case');
  CheckEqI(16463, TextToShortCut('CTRL + O'), 'upper case and spaces around +');
  CheckEqI(16463, TextToShortCut('  Ctrl  +O  '), 'surrounding spaces');
  CheckEqI($E041, TextToShortCut('Shift+Alt+Ctrl+A'), 'modifiers in any order');
  CheckEqI($A000 or VK_DELETE, TextToShortCut('alt + shift + del'), 'two modifiers, lower case');
  CheckEqI(VK_F10, TextToShortCut('F10'), 'no modifier');
  CheckEqI(scCtrl or $6B, TextToShortCut('Ctrl+Num +'), 'Num + after a modifier');
  CheckEqI($6B, TextToShortCut('num +'), 'Num + alone, lower case');
  CheckEqI(VK_NEXT, TextToShortCut('PgDn'), 'PgDn');
  CheckEqI(0, TextToShortCut(''), 'empty');
  CheckEqI(0, TextToShortCut('Ctrl'), 'modifier only');
  CheckEqI(0, TextToShortCut('Ctrl+'), 'modifier and + only');
  CheckEqI(0, TextToShortCut('Ctrl+Foo'), 'unknown key name');
  CheckEqI(0, TextToShortCut('Control+A'), 'unknown modifier name');
  CheckEqI(0, TextToShortCut('Delete'), 'only the fixed short names');
  CheckEqI(0, TextToShortCut('F25'), 'F25 unknown');
  CheckEqI(0, TextToShortCut('Ctrl+A+B'), 'two keys');
  CheckEqI(0, TextToShortCut(ShortCutToText(ShortCut(VK_OEM_1, []))), 'system names are not recognised');

  Section('round trip');
  Ok := True;
  Failures := '';
  for K := 1 to 255 do
    for M := 0 to 7 do
    begin
      SC := K or (M shl 13);
      if (Txt(K, []) <> '') and (TextToShortCut(Txt(K, [])) = K) then
        if TextToShortCut(ShortCutToText(SC)) <> SC then
        begin
          Ok := False;
          Failures := Failures + ' ' + IntToStr(SC);
        end;
    end;
  CheckEqS('', Failures, 'TextToShortCut(ShortCutToText(SC)) = SC for every fixed name and modifier set');
  Check(Ok, 'round trip ok');
end;

begin
  Run;
  Done;
end.
