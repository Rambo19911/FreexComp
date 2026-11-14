// covers: LANG-243, LANG-245, CMP-324
program StringConcat;
// '+' with strings, characters and literals: two operands (_UStrCat), three and more (_UStrCatN), characters converted
// first; the target may be an operand; concatenations used as values live in temporaries.
const
  Pair = 'ab';

function Wrap(const S: string): string;
begin
  Result := '<' + S + '>';
end;

function Repeated(C: Char; N: Integer): string;
var
  I: Integer;
begin
  Result := '';
  for I := 1 to N do
    Result := Result + C;
end;

var
  A, B, S: string;
  C, D: Char;
  I: Integer;
begin
  A := 'foo';
  B := 'bar';
  S := A + B;
  Writeln(S, ' ', Length(S));
  S := A + ' ' + B + '!';
  Writeln(S);
  C := 'x';
  D := 'y';
  S := C + D;
  Writeln(S, ' ', Length(S));
  S := A + C;
  Writeln(S);
  S := C + A + D;
  Writeln(S);
  S := 'lit' + 'eral';
  Writeln(S);
  S := '' + A + '';
  Writeln(S);
  S := A;
  S := S + S;
  Writeln(S);
  S := B + S;
  Writeln(S);
  Writeln(Wrap(A + B));
  Writeln(Wrap(Wrap('')));
  Writeln(Repeated(Pair[2], 5));
  S := '';
  for I := 1 to 10 do
    S := S + Chr(Ord('0') + I mod 10);
  Writeln(S, ' ', Length(S));
  Writeln(A + '-' + B, ' ', Length(A + B + A));
  S := A + B + C + D + 'z' + Wrap(B);
  Writeln(S);
end.
