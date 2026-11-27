program CaseDirectivesLower;

{$apptype CONSOLE}
{$define Trace}

begin
  {$ifdef Trace}
  Writeln('{$IFDEF in a string}');
  {$endif}
  {$r+}
end.
