// covers: LANG-420, LANG-421, ABI-080
program UntypedParams;

{ Untyped var/const/out parameters (passed as the variable's address): a byte dump of any variable, a swap of two
  variables of any type through Move, an out parameter cleared with FillChar, variable typecasts of the parameter
  (to a managed type too), and the parameter passed on to another untyped parameter. }

type
  TPair = record
    A, B: Word;
  end;
  TBytes = array[0..1023] of Byte;
  PBytes = ^TBytes;

procedure Dump(const Data; Size: Integer);
var
  P: PBytes;
  I: Integer;
begin
  P := @Data;
  for I := 0 to Size - 1 do
    Write(P^[I], ' ');
  Writeln('|');
end;

procedure Swap(var X, Y; Size: Integer);
var
  Buf: array[0..63] of Byte;
begin
  Move(X, Buf, Size);
  Move(Y, X, Size);
  Move(Buf, Y, Size);
end;

procedure Clear(out Data; Size: Integer);
begin
  FillChar(Data, Size, 0);
end;

procedure SetText(var S; const Value: string);
begin
  UnicodeString(S) := Value;
end;

function TextOf(const S): string;
begin
  Result := string(S) + '!';
end;

procedure Forward(const Data; Size: Integer);
begin
  Dump(Data, Size);
end;

procedure Bump(var N);
begin
  Inc(Integer(N), 10);
end;

const
  Answer: Integer = 42;

var
  I, J: Integer;
  P: TPair;
  S: string;
begin
  I := 258;
  Dump(I, SizeOf(I));
  J := -1;
  Swap(I, J, SizeOf(Integer));
  Writeln(I, ' ', J);
  P.A := 1;
  P.B := 2;
  Forward(P, SizeOf(P));
  Clear(P, SizeOf(P));
  Writeln(P.A, ' ', P.B);
  SetText(S, 'hello');
  Writeln(S, ' ', TextOf(S));
  Bump(J);
  Writeln(J);
  Dump(Answer, 1);
end.
