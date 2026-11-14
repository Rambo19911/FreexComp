// covers: LANG-242, ABI-030, ABI-031, ABI-032, CMP-325
program StringParams;
// value/var/const/out string parameters and string results: value parameters are the callee's own copy, results
// arrive through the hidden Result (into the target or a temporary, CMP-325 rule 4), discarded results are finalised.
var
  G: string;

procedure ByValue(S: string);
begin
  Writeln('value in: ', S);
  S := 'changed';
  Writeln('value now: ', S);
end;

procedure ByConst(const S: string);
begin
  Writeln('const: ', S, ' ', Length(S));
end;

procedure ByVar(var S: string);
begin
  Writeln('var in: ', S);
  S := 'from var';
end;

procedure ByOut(out S: string);
begin
  Writeln('out in: [', S, ']');
  S := 'from out';
end;

function Greeting: string;
begin
  Result := 'hello';
end;

function Echo(S: string): string;
begin
  Result := S;
end;

function Pick(Flag: Boolean; const A, B: string): string;
begin
  if Flag then
    Exit(A);
  Result := B;
end;

function Twice(const S: string): string;
begin
  Result := Echo(Echo(S));
end;

function Outer: string;
var
  Local: string;

  procedure SetLocal;
  begin
    Local := 'nested';
  end;

begin
  SetLocal;
  Result := Local;
end;

procedure Swap(var A, B: string);
var
  T: string;
begin
  T := A;
  A := B;
  B := T;
end;

procedure Locals;
var
  A, B: string;
begin
  A := Echo('x');
  B := Echo(A);
  A := Echo(A);
  Writeln(A, B);
end;

function Letter(C: Char): string;
begin
  Result := C;
end;

var
  S, T: string;
  I: Integer;
begin
  S := 'original';
  ByValue(S);
  Writeln('after value: ', S);
  ByConst(S);
  ByConst('literal');
  ByVar(S);
  Writeln('after var: ', S);
  ByOut(S);
  Writeln('after out: ', S);
  Writeln(Greeting);
  S := Greeting;
  Writeln(S);
  T := Echo(S);
  Writeln(T);
  S := Echo(S);
  Writeln(S);
  Writeln(Pick(True, 'yes', 'no'), ' ', Pick(False, 'yes', 'no'));
  Writeln(Twice('twice'));
  Writeln(Outer);
  S := 'left';
  T := 'right';
  Swap(S, T);
  Writeln(S, ' ', T);
  G := Echo('global');
  Writeln(G);
  for I := 1 to 3 do
    G := Echo(G);
  Writeln(G);
  Echo('discarded');
  ByConst(Echo('arg'));
  ByValue(Greeting);
  Locals;
  Writeln('[', Greeting:7, ']', Letter('q'), Length(Letter('r')));
end.
