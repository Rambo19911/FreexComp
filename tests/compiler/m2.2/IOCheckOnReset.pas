// covers: ABI-132
program IOCheckOnReset;
// Reset of a missing file records error 2 in InOutRes: {$I-} leaves it for IOResult (which reads and clears it);
// under {$I+} the _IOCheck after Reset turns it into runtime error 2 (the output before it is kept).
{$I+}
var
  F: Text;
begin
  AssignFile(F, 'missing.txt');
  {$I-}
  Reset(F);
  {$I+}
  Writeln('IOResult = ', IOResult);
  Writeln('again = ', IOResult);
  Reset(F);
  Writeln('not reached');
end.
