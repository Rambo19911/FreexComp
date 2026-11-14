// covers: LANG-450, LANG-452, LANG-402
program NestedProcs;

function SumOfSquares(N: Integer): Integer;
var
  Total: Integer;

  procedure Add(V: Integer);
  begin
    Total := Total + V * V;
  end;

var
  I: Integer;
begin
  Total := 0;
  for I := 1 to N do
    Add(I);
  Result := Total;
end;

procedure TwoLevels(Base: Integer);
var
  Level1: Integer;

  procedure Middle(Step: Integer);
  var
    Level2: Integer;

    procedure Inner;
    begin
      Level1 := Level1 + Base;
      Level2 := Level2 + Step;
      Writeln('inner: base=', Base, ' level1=', Level1, ' level2=', Level2);
    end;

  begin
    Level2 := 100;
    Inner;
    Inner;
    Writeln('middle: level2=', Level2);
  end;

begin
  Level1 := 0;
  Middle(7);
  Writeln('outer: level1=', Level1);
end;

function Counter(Start: Integer): Integer;

  function Inc10(X: Integer): Integer;
  begin
    Inc10 := X + 10;
  end;

  procedure SetResult;
  begin
    Counter := Inc10(Start);
  end;

begin
  SetResult;
  Result := Result + 1;
end;

procedure Modify(var X: Integer);

  procedure Deep;
  begin
    X := X * 3;
  end;

begin
  Deep;
  Deep;
end;

function Fact(N: Integer): Integer;

  function Go(K: Integer): Integer;
  begin
    if K > N then
      Go := 1
    else
      Go := K * Go(K + 1);
  end;

begin
  Fact := Go(1);
end;

procedure ShadowOuter;
var
  X: Integer;

  procedure Inner;
  var
    X: Integer;
  begin
    X := 5;
    Writeln('inner X=', X);
  end;

begin
  X := 1;
  Inner;
  Writeln('outer X=', X);
end;

var
  V: Integer;
begin
  Writeln(SumOfSquares(4));
  TwoLevels(3);
  Writeln(Counter(5));
  V := 2;
  Modify(V);
  Writeln(V);
  Writeln(Fact(6));
  ShadowOuter;
end.
