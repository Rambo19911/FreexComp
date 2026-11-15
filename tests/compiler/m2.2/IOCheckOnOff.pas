// covers: ABI-132, CMP-411
program IOCheckOnOff;
// {$I-}: I/O errors stay in InOutRes and the program continues; IOResult reads and clears them. A malformed number reads
// 0 and records 106; Reset of a missing file records 2; CloseFile of a file that is not open records 103.
// {$I+}: the _IOCheck after Reset turns error 2 into runtime error 2 (the output before it is kept).
var
  F: Text;
  I, E: Integer;
begin
  {$I-}
  I := 5;
  Read(I);
  E := IOResult;
  Writeln('Read: ', I, ' ', E);
  E := IOResult;
  Writeln('cleared: ', E);
  AssignFile(F, 'missing.txt');
  Reset(F);
  E := IOResult;
  Writeln('Reset: ', E);
  CloseFile(F);
  E := IOResult;
  Writeln('CloseFile: ', E);
  {$I+}
  Writeln('checked');
  Reset(F);
  Writeln('not reached');
end.
