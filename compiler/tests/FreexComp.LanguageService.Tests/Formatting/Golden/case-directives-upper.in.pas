program CaseDirectives;

{$apptype console}
{$define Trace}

begin
  {$ifdef Trace}
  Writeln('{$ifdef in a string}');
  {$else}
  {$ifdef nested} disabled {$endif}
  {$endif}
  (*$r+*)
  { $ifdef this is a comment }
  {$IfNDef Never}
  Writeln(1);
  {$EndIf}
end.
