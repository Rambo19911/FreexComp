// covers: LANG-110, LANG-115
PROGRAM CaseInsensitive;
VAR
  Counter: INTEGER;
  flag: boolean;

Procedure SHOWVALUE(value: Integer);
BEGIN
  WRITELN('value=', VALUE);
END;

FUNCTION twice(N: integer): InTeGeR;
begin
  RESULT := n * 2;
eNd;

Begin
  counter := 5;
  COUNTER := Counter + 1;
  ShowValue(cOuNtEr);
  showvalue(TWICE(counter));
  Flag := TRUE;
  IF FLAG AND (counter > 0) THEN
    writeln('flag ', FLAG, ' ', true, ' ', False)
  ELSE
    WriteLn('never');
  FOR counter := 1 TO 2 DO
    Write(COUNTER);
  WriteLN;
  COUNTER := 3;
  WHILE Counter < 5 Do Inc(COUNTER);
  writeln(Counter, ' ', MAXINT, ' ', high(BYTE), ' ', SIZEOF(int64));
eNd.
