// covers: LANG-385
program ErrInlineVarInfer;

procedure P(A: Integer);
begin
  var X := nil;
  var A := 1;
  begin
    var Y := 2;
    Writeln(Y);
  end;
  var Y := 3;
  var Y := 4;
  Writeln(Y);
end;

begin
  P(1);
end.