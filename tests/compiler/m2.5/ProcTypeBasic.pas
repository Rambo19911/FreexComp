// covers: LANG-480, LANG-482, LANG-483, LANG-484
program ProcTypeBasic;

{ Plain procedural values: variables, nil and Assigned, routines passed as arguments, a function value called
  implicitly, and the M2.5 sources — a class static method (by name, qualified, with @) and an overloaded routine
  whose overload is chosen by the target type. }

type
  TIntFunc = function(X: Integer): Integer;
  TStrFunc = function(const S: string): string;
  TGetter = function: Integer;
  TAction = procedure;

  TMath = class
    class function Square(X: Integer): Integer; static;
    class function Twice(X: Integer): Integer; static;
    class procedure Hello; static;
    class function Pick(Second: Boolean): TIntFunc; static;
  end;

class function TMath.Square(X: Integer): Integer;
begin
  Result := X * X;
end;

class function TMath.Twice(X: Integer): Integer;
begin
  Result := 3 * X;
end;

class procedure TMath.Hello;
begin
  Writeln('hello from TMath');
end;

class function TMath.Pick(Second: Boolean): TIntFunc;
begin
  if Second then
    Result := Twice
  else
    Result := Square;
end;

function Twice(X: Integer): Integer; overload;
begin
  Result := X + X;
end;

function Twice(const S: string): string; overload;
begin
  Result := S + S;
end;

function Seven: Integer;
begin
  Result := 7;
end;

function Apply(F: TIntFunc; X: Integer): Integer;
begin
  Result := F(X);
end;

var
  F: TIntFunc;
  G: TStrFunc;
  Get: TGetter;
  A: TAction;
begin
  F := nil;
  Writeln(Assigned(F));
  F := TMath.Square;
  Writeln(Assigned(F), ' ', F(5));
  F := @TMath.Twice;
  Writeln(F(5), ' ', Apply(TMath.Square, 3), ' ', Apply(F, 4));
  F := Twice;
  G := Twice;
  Writeln(F(21), ' ', G('ab'));
  Writeln(Apply(Twice, 10));
  A := TMath.Hello;
  A;
  F := TMath.Pick(False);
  Writeln(F(6), ' ', TMath.Pick(True)(6));
  Get := Seven;
  Writeln(Get + 1);
end.
