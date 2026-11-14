// covers: LANG-480, LANG-482, LANG-483, LANG-484, LANG-462, CMP-378, CMP-433
program ProcTypeGlobal;
type
  TIntFunc = function(X: Integer): Integer;
  TProc = procedure;
  TGetter = function: Integer;
  TStdProc = procedure(const S: string); stdcall;
  TCallback = procedure(const S: string);
  TOp = record
    Name: Char;
    F: TIntFunc;
  end;

var
  Counter: Integer;

function Twice(X: Integer): Integer;
begin
  Result := X * 2;
end;

function Square(X: Integer): Integer;
begin
  Result := X * X;
end;

procedure Tick;
begin
  Counter := Counter + 1;
end;

function FortyTwo: Integer;
begin
  Result := 42;
end;

procedure Say(const S: string);
begin
  Writeln('say ', S);
end;

function ApplyTwice(F: TIntFunc; X: Integer): Integer;
begin
  Result := F(F(X));
end;

function Choose(UseSquare: Boolean): TIntFunc;
begin
  if UseSquare then
    Result := Square
  else
    Result := @Twice;
end;

var
  F, G: TIntFunc;
  P: TProc;
  Get: TGetter;
  Std: TStdProc;
  Cb: TCallback;
  Ops: array[1..2] of TOp;
  Table: array[0..2] of TIntFunc;
  I, N: Integer;
begin
  F := Twice;
  Writeln(F(5));
  F := @Square;
  Writeln(F(5));
  G := F;
  Writeln(G(3), ' ', G = F, ' ', Assigned(G));
  G := nil;
  Writeln(G = nil, ' ', Assigned(G), ' ', G <> F);
  Writeln(ApplyTwice(Twice, 3), ' ', ApplyTwice(@Square, 3));
  Writeln(Choose(True)(4), ' ', Choose(False)(4));
  P := Tick;
  P;
  P();
  Writeln(Counter);
  // a parameterless function value in a value position is called (LANG-482)
  Get := FortyTwo;
  N := Get;
  Writeln(N, ' ', Get + 1, ' ', Get());
  // types differing only in a calling-convention directive are compatible (LANG-462)
  Std := Say;
  Std('std');
  Cb := Std;
  Cb('cb');
  Ops[1].Name := 'a';
  Ops[1].F := Twice;
  Ops[2].Name := 'b';
  Ops[2].F := Square;
  for I := 1 to 2 do
    Writeln(Ops[I].Name, ' ', Ops[I].F(I + 5));
  Table[0] := Twice;
  Table[1] := Square;
  Table[2] := nil;
  for I := 0 to 2 do
    if Assigned(Table[I]) then
      Writeln(I, ': ', Table[I](10))
    else
      Writeln(I, ': nil');
end.
