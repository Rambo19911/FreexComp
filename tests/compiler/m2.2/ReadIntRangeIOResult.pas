// covers: ABI-132, CMP-411, LANG-942
program ReadIntRangeIOResult;
// An integer outside the variable's range records 106 in InOutRes (C-233): {$I-} code sees it through IOResult and
// continues (the value is stored truncated); {$I+}: the _IOCheck after Read turns it into runtime error 106.
var
  B: Byte;
  E: Integer;
begin
  {$I-}
  Read(B);
  E := IOResult;
  Writeln('Byte: ', B, ' ', E);
  Read(B);
  E := IOResult;
  Writeln('next: ', B, ' ', E);
  {$I+}
  Read(B);
  Writeln('not reached ', B);
end.
