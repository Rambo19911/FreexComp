// covers: RTL-1050
// phase: 8
program Win_ResourceLanguage;
{ Windows unit, task R-254: FindResourceExW / FindResourceEx (kernel32) and LoadStringW / LoadString (user32) on the
  program's own resources (Win_ResourceLanguage.rc). RCDATA 1 exists in English - United States (1033) and German -
  Germany (1031): each language finds its own entry, French - France (1036) none. String 7 and 20 are read into a
  buffer, truncated into a short one, read through the read-only pointer form (buffer size 0), and a missing
  number gives 0. }
uses Windows, TestKit;

{$R Win_ResourceLanguage.rc}

{$POINTERMATH ON}

{ the two bytes of RCDATA 1 in Language as text, or '-' when the language has no entry }
function DataIn(Language: Word): string;
var
  Info: HRSRC;
  P: PAnsiChar;
begin
  Info := FindResourceExW(HMODULE(HInstance), RT_RCDATA, MakeIntResource(1), Language);
  if Info = 0 then
    Exit('-');
  P := PAnsiChar(LockResource(LoadResource(HMODULE(HInstance), Info)));
  Result := string(P[0]) + string(P[1]);
end;

var
  Buffer: array[0..15] of WideChar;
  Short: array[0..3] of WideChar;
  Text: PWideChar;
  Count: Integer;
  Typed: string;

begin
  Section('FindResourceExW');
  CheckEqS('en', DataIn(1033), 'RCDATA 1 in 1033');
  CheckEqS('de', DataIn(1031), 'RCDATA 1 in 1031');
  CheckEqS('-', DataIn(1036), 'RCDATA 1 has no entry in 1036');
  Check(FindResourceEx(0, RT_RCDATA, MakeIntResource(1), 1031) <> 0, 'FindResourceEx, module 0 is the program');
  Check(FindResourceExW(HMODULE(HInstance), RT_RCDATA, MakeIntResource(2), 1033) = 0, 'RCDATA 2 does not exist');

  Section('LoadStringW');
  Count := LoadStringW(HINST(HInstance), 7, @Buffer[0], Length(Buffer));
  CheckEqI(5, Count, 'string 7: characters copied');
  CheckEqS('seven', string(PWideChar(@Buffer[0])), 'string 7');
  CheckEqI(6, LoadString(HINST(HInstance), 20, @Buffer[0], Length(Buffer)), 'LoadString, string 20: characters copied');
  CheckEqS('twenty', string(PWideChar(@Buffer[0])), 'string 20');
  CheckEqI(3, LoadStringW(HINST(HInstance), 20, @Short[0], Length(Short)), 'string 20 truncated to a 4-character buffer');
  CheckEqS('twe', string(PWideChar(@Short[0])), 'the truncated string ends with a null');
  Text := nil;
  CheckEqI(6, LoadStringW(HINST(HInstance), 20, PWideChar(@Text), 0), 'buffer size 0: the length of the resource');
  Check(Text <> nil, 'buffer size 0: a pointer to the resource');
  SetLength(Typed, 6);
  Move(Text^, Typed[1], 6 * SizeOf(WideChar));
  CheckEqS('twenty', Typed, 'the resource the pointer names');
  CheckEqI(0, LoadStringW(HINST(HInstance), 8, @Buffer[0], Length(Buffer)), 'a missing string gives 0');
  Done;
end.
