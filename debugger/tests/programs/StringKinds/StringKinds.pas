program StringKinds;

var
  U, Z, L, E, C, Big: string;
  A: AnsiString;
  S: ShortString;
  P, NP: PChar;
  PA: PAnsiChar;
  CA: array[0..7] of Char;

begin
  U := 'Grüße ✓ ';
  U := U + '😀';
  Z := 'a' + #0 + 'b';
  L := 'x' + #13#10 + 'y';
  E := '';
  C := 'const';
  A := 'Grüße';
  S := 'short';
  P := PChar(U);
  PA := PAnsiChar(A);
  NP := nil;
  CA[0] := 'a';
  CA[1] := 'b';
  CA[2] := 'c';
  Big := StringOfChar('x', 1048576);
  WriteLn(Length(U), ' ', Length(A), ' ', Length(Big), ' ', CA[0]); // stop
  WriteLn(Z, L, E, C, S, P, PA, NP = nil);
end.
