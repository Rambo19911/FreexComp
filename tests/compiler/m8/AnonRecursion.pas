// covers: LANG-971, LANG-721, LANG-722
program AnonRecursion;

type
  TIntFunc = reference to function(N: Integer): Integer;
  TBoolFunc = reference to function(N: Integer): Boolean;
  TLevelFunc = reference to function(Depth: Integer): TIntFunc;
  TProc = reference to procedure;

  THolder = class
    FCount: Integer;
    FStep: TProc;
    constructor Create;
  end;

// Writeln writes a Boolean in upper case (LANG-921); the expected lines use this form
function Show(Value: Boolean): string;
begin
  if Value then Result := 'True' else Result := 'False';
end;

// the closure in the field captures Self: a plain reference, so no cycle
constructor THolder.Create;
begin
  FStep := procedure begin FCount := FCount + 1 end;
end;

// the anonymous method calls itself through the variable it is assigned to
function Factorial(N: Integer): Integer;
var
  Fact: TIntFunc;
begin
  Fact := function(K: Integer): Integer
    begin
      if K <= 1 then
        Result := 1
      else
        Result := K * Fact(K - 1);
    end;
  Result := Fact(N);
  Fact := nil;
end;

// two captured variables that call each other: each call reads the shared variable
procedure Parity;
var
  IsEven, IsOdd: TBoolFunc;
begin
  IsEven := function(K: Integer): Boolean
    begin
      if K = 0 then Result := True else Result := IsOdd(K - 1);
    end;
  IsOdd := function(K: Integer): Boolean
    begin
      if K = 0 then Result := False else Result := IsEven(K - 1);
    end;
  Writeln(Show(IsEven(10)), ' ', Show(IsEven(7)));
  IsEven := nil;
  IsOdd := nil;
end;

// a closure that returns a closure which calls the first one: Level is two frames away
procedure TwoLevels;
var
  Level: TLevelFunc;
  Top: TIntFunc;
begin
  Level := function(Depth: Integer): TIntFunc
    begin
      Result := function(K: Integer): Integer
        var
          Next: TIntFunc;
        begin
          if Depth = 0 then
            Result := K
          else
          begin
            Next := Level(Depth - 1);
            Result := Next(K * 2);
          end;
        end;
    end;
  Top := Level(3);
  Writeln(Top(5));
  Top := nil;
  Level := nil;
end;

// the frame holds Sum and Sum holds the frame: only storing nil breaks the cycle
function SumTo(N: Integer; Release: Boolean): Integer;
var
  Sum: TIntFunc;
begin
  Sum := function(K: Integer): Integer
    begin
      if K = 0 then Result := 0 else Result := K + Sum(K - 1);
    end;
  Result := Sum(N);
  if Release then
    Sum := nil;
end;

var
  Before: Integer;
  Holder: THolder;
begin
  Writeln(Factorial(5));
  Parity;
  TwoLevels;

  Before := _LiveAllocCount;
  Writeln(SumTo(4, True));
  Writeln('released: ', Show(_LiveAllocCount - Before = 0));

  Before := _LiveAllocCount;
  Writeln(SumTo(4, False));
  Writeln('kept by its cycle: ', Show(_LiveAllocCount - Before > 0));

  Before := _LiveAllocCount;
  Holder := THolder.Create;
  Holder.FStep();
  Holder.FStep();
  Writeln(Holder.FCount);
  Holder.Free;
  Writeln('object and closure released: ', Show(_LiveAllocCount - Before = 0));
end.
