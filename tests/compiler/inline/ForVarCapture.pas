// covers: LANG-577, CMP-1410, CMP-1411
program ForVarCapture;

{ An anonymous method that captures the variable of a for var loop captures the instance of its iteration: closures made in
  different iterations see different values, closures made in one iteration share the instance. Counted loops and for ... in
  loops (array, string, set, class enumerator) alike; a classic for loop over an outer variable keeps one shared variable.
  The iteration instances are freed once no closure holds them, however the loop was left. }

type
  TFunc = reference to function: Integer;
  TCharFunc = reference to function: Char;
  TProc = reference to procedure;
  TDigits = set of 0..9;

  TCountEnumerator = class
  private
    FCurrent, FLast: Integer;
  public
    constructor Create(ALast: Integer);
    function MoveNext: Boolean;
    property Current: Integer read FCurrent;
  end;

  TCounter = class
  private
    FLast: Integer;
  public
    constructor Create(ALast: Integer);
    function GetEnumerator: TCountEnumerator;
  end;

constructor TCountEnumerator.Create(ALast: Integer);
begin
  inherited Create;
  FLast := ALast;
end;

function TCountEnumerator.MoveNext: Boolean;
begin
  Inc(FCurrent);
  Result := FCurrent <= FLast;
end;

constructor TCounter.Create(ALast: Integer);
begin
  inherited Create;
  FLast := ALast;
end;

function TCounter.GetEnumerator: TCountEnumerator;
begin
  Result := TCountEnumerator.Create(FLast);
end;

{ the example of LANG-577 }
procedure Counted;
var
  Fs: array[1..3] of TFunc;
begin
  for var I := 1 to 3 do
    Fs[I] := function: Integer begin Result := I * 10 end;
  Writeln('counted ', Fs[1](), ' ', Fs[2](), ' ', Fs[3]());
end;

procedure CountedDown;
var
  Fs: array[1..3] of TFunc;
  N: Integer;
begin
  N := 0;
  for var I := 3 downto 1 do
  begin
    Inc(N);
    Fs[N] := function: Integer begin Result := I end;
  end;
  Writeln('downto ', Fs[1](), ' ', Fs[2](), ' ', Fs[3]());
end;

procedure OverArray;
var
  Values: array of Integer;
  Fs: array[0..2] of TFunc;
  N: Integer;
begin
  SetLength(Values, 3);
  Values[0] := 7;
  Values[1] := 8;
  Values[2] := 9;
  N := 0;
  for var X in Values do
  begin
    Fs[N] := function: Integer begin Result := X end;
    Inc(N);
  end;
  Writeln('array ', Fs[0](), ' ', Fs[1](), ' ', Fs[2]());
end;

procedure OverStringAndSet;
var
  Cs: array[0..2] of TCharFunc;
  Ds: array[0..2] of TFunc;
  Digits: TDigits;
  N: Integer;
begin
  N := 0;
  for var C in 'xyz' do
  begin
    Cs[N] := function: Char begin Result := C end;
    Inc(N);
  end;
  Writeln('string ', Cs[0](), Cs[1](), Cs[2]());
  Digits := [2, 5, 8];
  N := 0;
  for var D in Digits do
  begin
    Ds[N] := function: Integer begin Result := D end;
    Inc(N);
  end;
  Writeln('set ', Ds[0](), ' ', Ds[1](), ' ', Ds[2]());
end;

procedure OverEnumerator;
var
  Counter: TCounter;
  Fs: array[1..3] of TFunc;
begin
  Counter := TCounter.Create(3);
  for var K in Counter do
    Fs[K] := function: Integer begin Result := K * 100 end;
  Counter.Free;
  Writeln('enumerator ', Fs[1](), ' ', Fs[2](), ' ', Fs[3]());
end;

{ two closures of one iteration share its instance, and an inline variable of the body is a frame of its own inside it }
procedure SharedInOneIteration;
var
  Bump: array[1..2] of TProc;
  Get: array[1..2] of TFunc;
begin
  for var I := 1 to 2 do
  begin
    var Count := 0;
    Bump[I] := procedure begin Count := Count + I end;
    Get[I] := function: Integer begin Result := I * 1000 + Count end;
  end;
  Bump[1]();
  Bump[1]();
  Bump[2]();
  Writeln('shared ', Get[1](), ' ', Get[2]());
end;

{ nested captured loops: every inner iteration has its own instance chained to the outer iteration's }
procedure Nested;
var
  Fs: array[1..2, 1..2] of TFunc;
begin
  for var I := 1 to 2 do
    for var J := 1 to 2 do
      Fs[I, J] := function: Integer begin Result := I * 10 + J end;
  Writeln('nested ', Fs[1, 1](), ' ', Fs[1, 2](), ' ', Fs[2, 1](), ' ', Fs[2, 2]());
end;

{ a closure called inside its own iteration sees that iteration's value }
procedure CalledInside;
begin
  for var I := 1 to 3 do
  begin
    var F: TFunc := function: Integer begin Result := I * I end;
    Write(F(), ' ');
  end;
  Writeln('inside');
end;

var
  Made: array[1..2] of TFunc;

{ a captured loop inside an anonymous method: the iteration frames chain to that method's frame (the closures are kept in a
  global: one in a variable captured by an enclosing frame would form a reference cycle with it) }
procedure InsideAnonymous;
var
  Make: TProc;
  Offset: Integer;
begin
  Offset := 100;
  Make := procedure
    begin
      for var I := 1 to 2 do
        Made[I] := function: Integer begin Result := Offset + I end;
    end;
  Make();
  Make := nil;
  Writeln('in anonymous ', Made[1](), ' ', Made[2]());
  Made[1] := nil;
  Made[2] := nil;
end;

{ contrast: a classic for loop over an outer variable: every closure reads the one variable at call time }
procedure Classic;
var
  Fs: array[1..3] of TFunc;
  I: Integer;
begin
  for I := 1 to 3 do
    Fs[I] := function: Integer begin Result := I * 10 end;
  I := 4;
  Writeln('classic ', Fs[1](), ' ', Fs[2](), ' ', Fs[3]());
end;

var
  Kept: array[1..4] of TFunc;

{ leaving the loop by Break, Continue, Exit, an exception or goto releases the last iteration's instance }
procedure LeaveBy(Way: Integer);
label
  Done;
begin
  for var I := 1 to 4 do
  begin
    if I = 2 then
      Continue;
    Kept[I] := function: Integer begin Result := I end;
    if I = 3 then
    begin
      if Way = 1 then
        Break;
      if Way = 2 then
        Exit;
      if Way = 3 then
        raise TObject.Create;
      if Way = 4 then
        goto Done;
    end;
  end;
Done:
  Writeln('loop left ', Way);
end;

procedure ClearKept;
var
  K: Integer;
begin
  for K := 1 to 4 do
    Kept[K] := nil;
end;

procedure Leaks;
var
  Before: NativeInt;
  Way: Integer;
begin
  Before := _LiveAllocCount;
  Counted;
  OverArray;
  OverEnumerator;
  SharedInOneIteration;
  Nested;
  CalledInside;
  InsideAnonymous;
  for Way := 0 to 4 do
  begin
    try
      LeaveBy(Way);
    except
      on E: TObject do
        Writeln('raised ', Way);
    end;
    Writeln('kept ', Way, ': ', Kept[1](), ' ', Kept[3]());
    ClearKept;
  end;
  Writeln('leaked: ', _LiveAllocCount - Before);
end;

var
  Fs: array[1..3] of TFunc;
  Before: NativeInt;
begin
  Counted;
  CountedDown;
  OverArray;
  OverStringAndSet;
  OverEnumerator;
  SharedInOneIteration;
  Nested;
  CalledInside;
  InsideAnonymous;
  Classic;
  Leaks;

  { the same in the program block }
  Before := _LiveAllocCount;
  for var I := 1 to 3 do
    Fs[I] := function: Integer begin Result := I + 40 end;
  Writeln('main ', Fs[1](), ' ', Fs[2](), ' ', Fs[3]());
  Fs[1] := nil;
  Fs[2] := nil;
  Fs[3] := nil;
  Writeln('main leaked: ', _LiveAllocCount - Before);
end.
