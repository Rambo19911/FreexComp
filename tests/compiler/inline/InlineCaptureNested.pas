// covers: LANG-566, CMP-1183
program InlineCaptureNested;

{ Captured inline variables in two nested scopes with a closure inside the inner one using both and a routine parameter; an
  anonymous method that declares and captures its own inline variable; recursion; anonymous methods inside anonymous methods
  and a routine nested in one reaching the variables of the enclosing scopes; a capturing scope inside a finally part, in a
  constructor and a method (with Self) and in the program block. }

type
  TFunc = reference to function: Integer;
  TProc = reference to procedure;
  TMaker = reference to function(Delta: Integer): TFunc;

  TBox = class
  private
    FBase: Integer;
  public
    Fs: array[1..2] of TFunc;
    constructor Create(ABase: Integer);
    procedure Fill;
  end;

constructor TBox.Create(ABase: Integer);
var
  I: Integer;
begin
  inherited Create;
  FBase := ABase;
  for I := 1 to 2 do
  begin
    var Mine := I + FBase;
    Fs[I] := function: Integer begin Inc(Mine); Result := Mine + FBase end;
  end;
end;

procedure TBox.Fill;
var
  I: Integer;
begin
  for I := 1 to 2 do
  begin
    var Mine := I * 2;
    Fs[I] := function: Integer begin Inc(Mine); Result := Mine + FBase end;
  end;
end;

procedure TwoScopes(Base: Integer);
var
  I, J, A, B, C: Integer;
begin
  for I := 1 to 2 do
  begin
    var Outer := Base + I;
    var Fs: array[0..1] of TFunc;
    for J := 0 to 1 do
    begin
      var Inner := J * 100;
      Fs[J] := function: Integer
        begin
          Result := Outer + Inner + Base;
          Inc(Outer);
          Inc(Inner, 10);
        end;
    end;
    A := Fs[0]();
    B := Fs[1]();
    C := Fs[0]();
    Writeln('two scopes ', A, ' ', B, ' ', C, ' ', Outer);
  end;
end;

procedure OwnVariable;
var
  Run: TProc;
begin
  Run := procedure
    var
      Gs: array[0..1] of TFunc;
      K, A, B, C: Integer;
    begin
      for K := 0 to 1 do
      begin
        var Mine := K + 1;
        Gs[K] := function: Integer begin Mine := Mine * 2; Result := Mine end;
      end;
      A := Gs[0]();
      B := Gs[1]();
      C := Gs[0]();
      Writeln('own ', A, ' ', B, ' ', C);
    end;
  Run();
  Run();
end;

function Sum(Depth: Integer): Integer;
begin
  Result := 0;
  if Depth > 0 then
  begin
    var Level := Depth;
    var Add: TFunc := function: Integer begin Result := Level * 10 end;
    Result := Sum(Depth - 1);
    Result := Result + Add() + Level;
  end;
end;

{ a closure made by a closure: the inner one reaches the scope's variable through the frames in between }
procedure Makers;
var
  Ms: array[1..2] of TMaker;
  I, A, B, C: Integer;
  F, G: TFunc;
begin
  for I := 1 to 2 do
  begin
    var Seed := I * 1000;
    Ms[I] := function(Delta: Integer): TFunc
      begin
        Result := function: Integer begin Seed := Seed + Delta; Result := Seed end;
      end;
  end;
  F := Ms[1](1);
  G := Ms[1](10);
  A := F();
  B := G();
  C := Ms[2](5)();
  Writeln('makers ', A, ' ', B, ' ', C);
end;

{ a routine nested in an anonymous method, and an anonymous method inside that routine }
procedure NestedRoutine;
var
  Ps: array[1..2] of TFunc;
  I, A, B, C: Integer;
begin
  for I := 1 to 2 do
  begin
    var Hits := I * 10;
    Ps[I] := function: Integer
      function Twice: Integer;
      var
        Again: TFunc;
      begin
        Inc(Hits);
        Again := function: Integer begin Inc(Hits); Result := Hits end;
        Result := Again();
      end;
      begin
        Result := Twice;
      end;
  end;
  A := Ps[1]();
  B := Ps[2]();
  C := Ps[1]();
  Writeln('nested routine ', A, ' ', B, ' ', C);
end;

{ a list that starts capturing at its second declaration, and a later captured declaration of the same list }
procedure OneList;
var
  Fs: array[1..2] of TFunc;
  I, A, B, C: Integer;
begin
  for I := 1 to 2 do
  begin
    var Plain := I;
    var First := Plain * 2;
    var Early: TFunc := function: Integer begin Result := First end;
    var Second := First + 1;
    Fs[I] := function: Integer begin Inc(First); Inc(Second, 2); Result := First * 100 + Second end;
    A := Early();
    B := Fs[I]();
    C := Early();
    Writeln('one list ', Plain, ' ', A, ' ', B, ' ', C);
  end;
  A := Fs[1]();
  B := Fs[2]();
  Writeln('one list after ', A, ' ', B);
end;

procedure InFinally;
var
  Fs: array[1..2] of TFunc;
  I, A, B, C: Integer;
begin
  try
    Writeln('try');
  finally
    for I := 1 to 2 do
    begin
      var Late := I + 40;
      Fs[I] := function: Integer begin Result := Late; Inc(Late) end;
    end;
    A := Fs[1]();
    B := Fs[2]();
    C := Fs[1]();
    Writeln('finally ', A, ' ', B, ' ', C);
  end;
end;

procedure Boxes;
var
  Box: TBox;
  A, B, C: Integer;
begin
  Box := TBox.Create(100);
  A := Box.Fs[1]();
  B := Box.Fs[2]();
  C := Box.Fs[1]();
  Writeln('constructor ', A, ' ', B, ' ', C);
  Box.Fill;
  A := Box.Fs[1]();
  B := Box.Fs[2]();
  C := Box.Fs[1]();
  Writeln('method ', A, ' ', B, ' ', C);
  Box.Free;
end;

var
  Before: NativeInt;
  Ps: array[1..2] of TFunc;
  I, A, B, C: Integer;
begin
  Before := _LiveAllocCount;
  TwoScopes(1000);
  OwnVariable;
  Writeln('sum ', Sum(3));
  Makers;
  NestedRoutine;
  OneList;
  InFinally;
  Boxes;

  { the program block: the scope's variable through an anonymous method, a routine nested in it and an anonymous method of
    that routine; the anonymous method has a capturing scope of its own }
  for I := 1 to 2 do
  begin
    var Hits := I * 10;
    Ps[I] := function: Integer
      function Twice: Integer;
      var
        Again: TFunc;
      begin
        Inc(Hits);
        Again := function: Integer begin Inc(Hits); Result := Hits end;
        Result := Again();
      end;
      begin
        var Own := Twice;
        var Add: TFunc := function: Integer begin Result := Own + Hits end;
        Result := Add();
      end;
  end;
  A := Ps[1]();
  B := Ps[2]();
  C := Ps[1]();
  Writeln('program block ', A, ' ', B, ' ', C);
  Ps[1] := nil;
  Ps[2] := nil;
  Writeln('leaked: ', _LiveAllocCount - Before);
end.
