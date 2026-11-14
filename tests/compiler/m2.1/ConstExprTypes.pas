// covers: LANG-126, LANG-341, LANG-222
program ConstExprTypes;
const
  A = 2147483647;
  B = 2147483648;
  C = A + 1;
  D = -2147483648;
  E = -2147483649;
  E2 = D - 1;
  F = 9223372036854775807;
  G = 9223372036854775808;
  H = F + 1;
  I = G - 1;
  J = 18446744073709551615;
  K = $FFFFFFFF;
  K2 = K + 0;
  L = Byte(300);
  M = MaxInt * 2;
  N = -F - 1;
  P = High(UInt64) div 2;
var
  V: Integer;
  W: Int64;
begin
  Writeln(SizeOf(A), SizeOf(B), SizeOf(C), SizeOf(D), SizeOf(E), SizeOf(E2));
  Writeln(SizeOf(F), SizeOf(G), SizeOf(H), SizeOf(I), SizeOf(J));
  Writeln(SizeOf(K), SizeOf(K2), SizeOf(L), SizeOf(M), SizeOf(N), SizeOf(P));
  Writeln(A, ' ', B, ' ', C, ' ', D, ' ', E, ' ', E2);
  Writeln(F, ' ', G, ' ', H, ' ', I, ' ', J);
  Writeln(K, ' ', K2, ' ', L, ' ', M, ' ', N, ' ', P);
  Writeln(A + 1, ' ', D - 1, ' ', A * A);
  V := A;
  Writeln(V + 1);
  W := A;
  Writeln(W + 1);
  W := -1;
  Writeln(W + J, ' ', W + F);
end.
