// covers: LANG-400, LANG-352
program ProcNoParams;
var
  Counter: Integer;

procedure Hello;
begin
  Writeln('hello');
end;

procedure Tick();
begin
  Counter := Counter + 1;
end;

function Next: Integer;
begin
  Counter := Counter + 1;
  Result := Counter;
end;

function Answer(): Integer;
begin
  Answer := 42;
end;

begin
  Hello;
  Hello();
  Tick;
  Tick();
  Writeln(Counter);
  Writeln(Next, ' ', Next(), ' ', Next + Next);
  Next;
  Next();
  Writeln(Counter);
  Writeln(Answer, ' ', Answer() * 2);
end.
