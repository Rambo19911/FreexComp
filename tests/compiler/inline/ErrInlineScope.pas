// covers: LANG-560, LANG-569, LANG-570
program ErrInlineScope;

{ An inline name exists only inside its declaration scope: a use before the declaration, in the declaration's own
  initialiser or after the block is E2003. Redeclaring a name of the same routine is E2004: in the same list, of an
  enclosing block, of a parameter and of a classic local. }

procedure Before;
begin
  Early := 1;
  var Early := 2;
  WriteLn(Early);
end;

procedure OwnInitialiser;
begin
  var Fresh := Fresh + 1;
  WriteLn(Fresh);
end;

procedure AfterBlock;
begin
  begin
    var Inner := 1;
    WriteLn(Inner);
  end;
  WriteLn(Inner);
  repeat
    var Line := 1;
  until Line = 1;
  WriteLn(Line);
end;

procedure Duplicates(Param: Integer);
var
  Classic: Integer;
begin
  Classic := Param;
  var Twice := 1;
  var Twice := 2;
  begin
    var Twice := 3;
    var Param := 4;
    const Classic = 5;
  end;
  for var Twice := 0 to 1 do
    WriteLn(Twice);
  WriteLn(Classic);
end;

begin
  Before;
  OwnInitialiser;
  AfterBlock;
  Duplicates(1);
end.
