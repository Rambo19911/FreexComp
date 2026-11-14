// covers: LANG-462, LANG-463, LANG-470
program ExternalWinApi;
{ C-077: external and public name across units. Clock imports from kernel32 (its .def comes from the RTL); Chars imports
  from user32 through the user32.def next to it (CMP-620 step 2); Gfx imports from gdi32 through its $L directive
  naming defs\gdi32.def (step 1). Twice and SharedCounter are reached through the link names Clock gives them with public name. }
uses Clock, Chars, Gfx;

function Twice(X: Integer): Integer; external name 'clock_twice';

var
  Start: Cardinal;
begin
  Start := TickCount;
  Writeln(ProcessId > 0);
  Writeln(TickCount >= Start);
  Writeln(Twice(21));
  Inc(SharedCounter);
  Writeln(SharedCounter);
  Writeln(IsAlpha('A'), ' ', IsAlpha('7'));
  Writeln(IsUpper('Q'), ' ', IsUpper('q'));
  Writeln(StockBrush <> 0);
end.