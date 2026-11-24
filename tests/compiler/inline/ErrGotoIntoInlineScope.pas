// covers: LANG-571
program ErrGotoIntoInlineScope;

{ A goto may not jump from outside into the declaration scope of an inline variable or constant (E2076): forward over a
  declaration of the same statement list, into a nested block past its declaration, into the scope of an inline constant.
  Jumping backward to a label before the declaration, out of a scope and inside one scope is allowed. }

procedure OverDeclaration;
label
  Skip;
begin
  goto Skip;
  var S: string := 'never';
Skip:
  Writeln(S);
end;

procedure IntoBlock(N: Integer);
label
  Before, Inside, After;
begin
  if N > 0 then
    goto Inside;
  begin
  Before:
    Writeln('before');
    var Count := N;
  Inside:
    Writeln(Count);
    if Count < 0 then
      goto Before;
    if Count = 0 then
      goto After;
  end;
After:
  Writeln('done');
end;

procedure IntoConstScope;
label
  Use, Done;
begin
  goto Use;
  const Limit = 10;
  const Typed: Integer = 3;
Use:
  Writeln(Limit + Typed);
  goto Done;
Done:
end;

procedure PastTwo;
label
  Target;
begin
  var First := 1;
  goto Target;
  var Second := 2;
  begin
    var Third := 3;
  Target:
    Writeln(First + Second + Third);
  end;
end;

begin
  OverDeclaration;
  IntoBlock(1);
  IntoConstScope;
  PastTwo;
end.
