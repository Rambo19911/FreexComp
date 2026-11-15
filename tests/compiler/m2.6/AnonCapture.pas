// covers: LANG-721, LANG-722, CMP-380
program AnonCapture;

type
  TIntFunc = reference to function(x: Integer): Integer;
  TStrFunc = reference to function(Name: string): string;

function MakeAdder(n: Integer): TIntFunc;
begin
  Result := function(x: Integer): Integer begin Result := x + n; n := n + 1 end;
end;

function MakeGreeter(const Greeting: string): TStrFunc;
var
  Suffix: string;
begin
  Suffix := '!';
  Result := function(Name: string): string begin Result := Greeting + ', ' + Name + Suffix end;
  Suffix := '?';
end;

var
  f, g: TIntFunc;
  h: TStrFunc;
begin
  f := MakeAdder(10);
  Writeln(f(1), ' ', f(1));
  g := MakeAdder(100);
  Writeln(g(1), ' ', f(1), ' ', g(5));
  h := MakeGreeter('Hello');
  Writeln(h('Ann'));
end.