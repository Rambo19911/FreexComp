// covers: RTL-007
program Aliases_ScopedUsesCompiles;
{ Unit alias table (RTL-007, LANG-533): a program names every shipped unit by its scoped name and uses one
  symbol of each. The unit list grows with every task that ships a unit (task R-123). }
uses System.Types, System.RTLConsts, System.SysUtils, Winapi.Windows, Winapi.Messages, System.Math,
  System.StrUtils, System.DateUtils, System.Character, System.TypInfo,
  System.Classes, System.IniFiles, System.IOUtils, System.NetEncoding;

begin
  Writeln(RectWidth(Rect(1, 2, 11, 5)));
  Writeln(SInvalidInteger);
  Writeln(IntToHex(255, 4));
  Writeln(MAX_PATH);
  Writeln(MakeLong(1, 2));
  Writeln(WM_USER);
  Writeln(Max(3, 8));
  Writeln(ReverseString('abc'));
  Writeln(DaysInAMonth(2024, 2));
  Writeln(IsLetter('x'));
  Writeln(GetEnumName(TypeInfo(Boolean), 1));
  Writeln(SizeOf(TNotifyEvent));
  Writeln(TMemIniFile.ClassName);
  Writeln(TPath.Combine('a', 'b'));
  Writeln(TNetEncoding.Base64String.Encode('foo'));
end.
