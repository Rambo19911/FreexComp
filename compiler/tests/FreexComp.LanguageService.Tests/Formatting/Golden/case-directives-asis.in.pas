program CaseDirectivesAsIs;

{$AppType Console}

begin
  {$IfDef Trace}
  Writeln('trace');
  {$EndIf}
end.
