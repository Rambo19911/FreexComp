program CaseDirectives;

{$APPTYPE console}
{$DEFINE Trace}

begin
  {$IFDEF Trace}
  Writeln('{$ifdef in a string}');
  {$ELSE}
  {$ifdef nested} disabled {$endif}
  {$ENDIF}
  (*$R+*)
  { $ifdef this is a comment }
  {$IFNDEF Never}
  Writeln(1);
  {$ENDIF}
end.
