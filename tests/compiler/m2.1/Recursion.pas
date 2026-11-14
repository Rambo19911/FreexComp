// covers: LANG-403, LANG-460
program Recursion;

function Factorial(N: Integer): Int64;
begin
  if N <= 1 then
    Result := 1
  else
    Result := N * Factorial(N - 1);
end;

function Fib(N: Integer): Integer;
begin
  if N < 2 then
    Fib := N
  else
    Fib := Fib(N - 1) + Fib(N - 2);
end;

function IsOdd(N: Integer): Boolean; forward;

function IsEven(N: Integer): Boolean;
begin
  if N = 0 then
    Result := True
  else
    Result := IsOdd(N - 1);
end;

function IsOdd(N: Integer): Boolean;
begin
  if N = 0 then
    Result := False
  else
    Result := IsEven(N - 1);
end;

procedure Countdown(N: Integer); forward;

procedure Countdown;
begin
  Write(N);
  if N > 0 then
  begin
    Write(',');
    Countdown(N - 1);
  end;
end;

function Ackermann(M, N: Integer): Integer;
begin
  if M = 0 then
    Result := N + 1
  else if N = 0 then
    Result := Ackermann(M - 1, 1)
  else
    Result := Ackermann(M - 1, Ackermann(M, N - 1));
end;

function Depth(N: Integer): Integer;
begin
  if N = 0 then
    Result := 0
  else
    Result := 1 + Depth(N - 1);
end;

var
  I: Integer;
begin
  Writeln(Factorial(1), ' ', Factorial(5), ' ', Factorial(20));
  for I := 0 to 10 do
    Write(Fib(I), ' ');
  Writeln('|');
  Writeln(Fib(25));
  Writeln(IsEven(10), ' ', IsOdd(7), ' ', IsEven(7), ' ', IsOdd(0));
  Countdown(5);
  Writeln;
  Writeln(Ackermann(2, 3), ' ', Ackermann(3, 3));
  Writeln(Depth(10000));
end.
