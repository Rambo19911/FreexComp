// covers: LANG-246, LANG-951
program ShortStringEdits;
type
  TRec = record Tag: Byte; Name: string[8]; end;
var
  N: string[10];
  H: ShortString;
  A: AnsiString;
  U: UTF8String;
  R: array[0..1] of TRec;
  I: Integer;
function Idx: Integer;
begin
  Inc(I);
  Result := I;
end;

begin
  N := 'abcdef';
  Delete(N, 2, 3);
  Writeln(N, ' ', Length(N));
  Insert('XYZ', N, 2);
  Writeln(N, ' ', Length(N));
  Insert('0123456789', N, 1);
  Writeln(N, ' ', Length(N));
  Str(-42:6, N);
  Writeln('[', N, '] ', Length(N));
  H := 'hello';
  SetLength(H, 3);
  Writeln(H, ' ', Length(H));
  SetLength(H, 5);
  Writeln(H, ' ', Length(H));
  SetLength(N, 300);
  Writeln(Length(N));
  SetLength(N, -1);
  Writeln(Length(N));
  A := 'Grüße';
  Delete(A, 3, 1);
  Writeln(A, ' ', Length(A));
  Insert('ü', A, 3);
  Writeln(A, ' ', Length(A));
  Str(3.5:5:1, A);
  Writeln('[', A, ']');
  U := 'x';
  Insert('é', U, 2);
  Writeln(U, ' ', Length(U));
  I := -1;
  R[1].Name := 'abc';
  Delete(R[Idx].Name, 1, 1);
  Writeln(R[1].Name, ' ', I);
  Str(I, R[0].Name);
  Writeln(R[0].Name);
end.
