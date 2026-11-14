// covers: RTL-065
program Sys_MoveFill;
{ Counts are held in variables so that the code generator calls memmove/memset instead of expanding them inline. }

var
  A, B: array[0..15] of Byte;
  S: array[0..7] of AnsiChar;
  W: array[0..7] of WideChar;
  N: Integer;

procedure Reset;
var
  K: Integer;
begin
  for K := 0 to 15 do
    A[K] := K;
end;

procedure Dump;
var
  K: Integer;
begin
  for K := 0 to 15 do
  begin
    Write(A[K]);
    if K < 15 then
      Write(' ');
  end;
  Writeln;
end;

begin
  Reset;
  N := 8;
  Move(A[0], A[4], N);
  Write('move up:    ');
  Dump;

  Reset;
  Move(A[4], A[0], N);
  Write('move down:  ');
  Dump;

  Reset;
  N := 5;
  FillChar(A[2], N, 255);
  Write('fill 255:   ');
  Dump;

  Reset;
  N := 2;
  FillChar(A[0], N, 'A');
  FillChar(A[14], N, True);
  Write('fill chars: ');
  Dump;

  Reset;
  N := 0;
  Move(A[0], A[8], N);
  FillChar(A[0], N, 9);
  N := -3;
  Move(A[0], A[8], N);
  FillChar(A[0], N, 9);
  Write('counts <=0: ');
  Dump;

  Reset;
  N := 16;
  memcpy(@B[0], @A[0], N);
  memset(@A[0], 7, 3);
  Write('memset 7:   ');
  Dump;
  Writeln('memcmp equal ', memcmp(@A[3], @B[3], 13) = 0);
  Writeln('memcmp order ', memcmp(@A[0], @B[0], 16) > 0, ' ', memcmp(@B[0], @A[0], 16) < 0);
  A[5] := 255;
  B[5] := 1;
  Writeln('memcmp bytes are unsigned ', memcmp(@A[5], @B[5], 1) > 0);
  Writeln('memcmp empty ', memcmp(@A[0], @B[0], 0));
  Writeln('memmove returns Dest ', memmove(@A[0], @B[0], 0) = @A[0]);

  S[0] := 'a';
  S[1] := 'b';
  S[2] := 'c';
  S[3] := #0;
  W[0] := 'x';
  W[1] := #0;
  Writeln('strlen ', strlen(@S[0]), ' ', strlen(@S[3]));
  Writeln('wcslen ', wcslen(@W[0]), ' ', wcslen(@W[1]));
end.
