PROGRAM CaseKeywordsUpper;

PROCEDURE Show(CONST Value: STRING); OVERLOAD;
BEGIN
  IF Value <> '' THEN
    Writeln(Value) // begin end
  ELSE
    Writeln('empty');
END;

VAR
  I: Integer;
BEGIN
  FOR I := 1 TO 2 DO
    Show('begin');
  REPEAT
    I := I DIV 2;
  UNTIL I = 0;
END.
