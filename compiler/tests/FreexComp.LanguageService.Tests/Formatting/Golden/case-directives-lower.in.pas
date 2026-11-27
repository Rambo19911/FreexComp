program CaseDirectivesLower;

{$APPTYPE CONSOLE}
{$DEFINE Trace}

begin
  {$IFDEF Trace}
  Writeln('{$IFDEF in a string}');
  {$ENDIF}
  {$R+}
end.
