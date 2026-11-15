// covers: LANG-720, LANG-723, LANG-724, ABI-097
program AnonBasic;

type
  TProc = reference to procedure;
  TIntProc = reference to procedure(X: Integer);
  TIntFunc = reference to function(X: Integer): Integer;
  TGreet = reference to function: string;
  TSub = reference to function(const L, R: Integer): Integer;

procedure RunTwice(const P: TProc);
begin
  P();
  P;
end;

function Apply(F: TIntFunc; X: Integer): Integer;
begin
  Result := F(X);
end;

var
  P: TProc;
  Q: TIntProc;
  F, G: TIntFunc;
  S: TGreet;
  D: TSub;
begin
  P := procedure begin Writeln('hello') end;
  P();
  RunTwice(P);
  RunTwice(procedure begin Writeln('inline') end);
  Q := procedure(X: Integer) begin Writeln('Q ', X) end;
  Q(5);
  F := function(X: Integer): Integer begin Result := X * X end;
  Writeln(F(7));
  Writeln(Apply(F, 3));
  Writeln(Apply(function(X: Integer): Integer begin Result := X + 100 end, 1));
  G := F;
  Writeln(G(4));
  S := function: string begin Result := 'greet' end;
  Writeln(S);
  Writeln(S() + '!');
  D := function(const L, R: Integer): Integer begin Result := L - R end;
  Writeln(D(10, 3));
  P := nil;
  Writeln(Assigned(P), ' ', Assigned(F), ' ', F <> nil);
end.