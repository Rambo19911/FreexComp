// covers: RTL-007
// opc: -U ../../winapi/src
program Aliases_ScopedUsesCompiles;
{ Unit alias table (RTL-007, LANG-533): a program names every shipped unit by its scoped name and uses one
  symbol of each. The unit list grows with every task that ships a unit (task R-123). }
uses System.Types, System.RTLConsts, Winapi.Windows;

begin
  Writeln(RectWidth(Rect(1, 2, 11, 5)));
  Writeln(SInvalidInteger);
  Writeln(MAX_PATH);
  Writeln(MakeLong(1, 2));
end.
