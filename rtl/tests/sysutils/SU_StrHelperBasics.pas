// phase: 8
// covers: RTL-1260, RTL-1263, RTL-1267, RTL-1268
program SU_StrHelperBasics;
{ sysutils.md section 12, task R-271 (F10-030): the string helper, part 1 - the properties Length and Chars
  (zero-based, strict: EArgumentOutOfRangeException with 'Index out of range (<i>)'), IsEmpty, CompareTo, Equals (and
  the class function Equals(A, B) of task R-273, which shares the name), the case members (= AnsiUpperCase /
  AnsiLowerCase, the plain and the Invariant names alike), Trim / TrimLeft / TrimRight with and without characters
  and PadLeft / PadRight (block G of standard-helpers.md section 5.2). Every receiver is a variable; no member
  changes it. Samples: A = 'Hello, World', U = 'Gr' u-umlaut sharp-s 'e ' Zhe i (Cyrillic),
  P = 'a' U+1F600 (a pair) 'b', E = ''. }
uses
  SysUtils, TestKit;

var
  A, U, P, E, S, T, V, W, N: string;

procedure CharFails(const R: string; Index: Integer; const Name: string);
var
  C: Char;
begin
  Expect('EArgumentOutOfRangeException', 'Index out of range (' + IntStr(Index) + ')');
  try
    C := R.Chars[Index];
    Writeln('# no exception, ', Ord(C));
  except
    on X: Exception do CheckRaises(Name, X.ClassName, X.Message);
  end;
end;

begin
  A := 'Hello, World';
  U := 'Gr' + #$00FC + #$00DF + 'e ' + #$0416 + #$0438;
  P := 'a' + #$D83D + #$DE00 + 'b';
  E := '';
  S := 'abc';

  Section('Length');
  CheckEqI(12, A.Length, 'A.Length');
  CheckEqI(8, U.Length, 'U.Length');
  CheckEqI(4, P.Length, 'P.Length (a pair counts two)');
  CheckEqI(0, E.Length, 'E.Length');

  Section('Chars');
  CheckEqI(Ord('H'), Ord(A.Chars[0]), 'A.Chars[0]');
  CheckEqI(Ord('d'), Ord(A.Chars[11]), 'A.Chars[11]');
  CheckEqI($D83D, Ord(P.Chars[1]), 'P.Chars[1] is the high surrogate');
  CheckEqI($00DF, Ord(U.Chars[3]), 'U.Chars[3]');
  CharFails(A, 12, 'A.Chars[12]');
  CharFails(A, -1, 'A.Chars[-1]');
  CharFails(E, 0, 'E.Chars[0]');
  CheckEqI(Ord('H'), Ord(A[1]), 'A[1] stays one-based');

  Section('IsEmpty');
  CheckEqB(True, E.IsEmpty, 'E.IsEmpty');
  CheckEqB(False, A.IsEmpty, 'A.IsEmpty');
  CheckEqB(False, P.IsEmpty, 'P.IsEmpty');

  Section('CompareTo');
  CheckEqI(-1, S.CompareTo('abd'), 'abc.CompareTo(abd)');
  CheckEqI(1, S.CompareTo('ab'), 'abc.CompareTo(ab)');
  CheckEqI(0, S.CompareTo('abc'), 'abc.CompareTo(abc)');
  CheckEqI(1, S.CompareTo('ABC'), 'abc.CompareTo(ABC) is ordinal');
  CheckEqI(-1, E.CompareTo('a'), 'E.CompareTo(a)');
  CheckEqI(0, E.CompareTo(''), 'E.CompareTo(E)');
  CheckEqI(1, U.CompareTo('Gr'), 'U.CompareTo(Gr)');

  Section('Equals');
  CheckEqB(True, S.Equals('abc'), 'abc.Equals(abc)');
  CheckEqB(False, S.Equals('ABC'), 'abc.Equals(ABC) is ordinal');
  CheckEqB(True, E.Equals(''), 'E.Equals(E)');
  CheckEqB(True, P.Equals('a' + #$D83D + #$DE00 + 'b'), 'P.Equals(P)');
  CheckEqB(True, TStringHelper.Equals(S, 'abc'), 'TStringHelper.Equals(abc, abc)');
  CheckEqB(False, TStringHelper.Equals(S, 'ABC'), 'TStringHelper.Equals(abc, ABC)');

  Section('case');
  CheckEqS('HELLO, WORLD', A.ToUpper, 'A.ToUpper');
  CheckEqS('hello, world', A.ToLower, 'A.ToLower');
  V := 'GR' + #$00DC + #$00DF + 'E ' + #$0416 + #$0418;
  W := 'gr' + #$00FC + #$00DF + 'e ' + #$0436 + #$0438;
  CheckEqS(V, U.ToUpper, 'U.ToUpper');
  CheckEqS(W, U.ToLower, 'U.ToLower');
  CheckEqS(V, U.ToUpperInvariant, 'U.ToUpperInvariant');
  CheckEqS(W, U.ToLowerInvariant, 'U.ToLowerInvariant');
  CheckEqS('A' + #$D83D + #$DE00 + 'B', P.ToUpper, 'P.ToUpper (the pair has no mapping)');
  CheckEqS('a' + #$D83D + #$DE00 + 'b', P.ToLowerInvariant, 'P.ToLowerInvariant');
  T := #$D801 + #$DC28;
  CheckEqS(#$D801 + #$DC00, T.ToUpper, 'U+10428.ToUpper = U+10400');
  T := #$D801 + #$DC00;
  CheckEqS(#$D801 + #$DC28, T.ToLower, 'U+10400.ToLower = U+10428');
  CheckEqS('', E.ToUpper, 'E.ToUpper');
  CheckEqS('', E.ToLower, 'E.ToLower');
  CheckEqS('', E.ToUpperInvariant, 'E.ToUpperInvariant');
  CheckEqS('', E.ToLowerInvariant, 'E.ToLowerInvariant');
  CheckEqS('Hello, World', A, 'A is unchanged');

  Section('Trim');
  T := '  a b ' + #9;
  CheckEqS('a b', T.Trim, 'T.Trim');
  CheckEqS('a b ' + #9, T.TrimLeft, 'T.TrimLeft');
  CheckEqS('  a b', T.TrimRight, 'T.TrimRight');
  CheckEqS('  a b ' + #9, T, 'T is unchanged');
  V := 'xyhiyx';
  CheckEqS('hi', V.Trim(['x', 'y']), 'xyhiyx.Trim([x, y])');
  CheckEqS('yhiyx', V.TrimLeft(['x']), 'xyhiyx.TrimLeft([x])');
  CheckEqS('xyhiy', V.TrimRight(['x']), 'xyhiyx.TrimRight([x])');
  CheckEqS('xyhiyx', V, 'xyhiyx is unchanged');
  W := ' hi ';
  CheckEqS(' hi ', W.Trim(['x']), ' hi .Trim([x]) keeps the spaces');
  CheckEqS(' hi', W.TrimRight([' ']), ' hi .TrimRight([space])');
  CheckEqS('a b', T.Trim([]), 'T.Trim([]) = T.Trim');
  CheckEqS('a b ' + #9, T.TrimLeft([]), 'T.TrimLeft([]) = T.TrimLeft');
  CheckEqS('  a b', T.TrimRight([]), 'T.TrimRight([]) = T.TrimRight');
  N := #$00A0 + 'a' + #$00A0;
  CheckEqS(N, N.Trim, 'NBSP a NBSP.Trim is unchanged');
  CheckEqS('a', N.Trim([#$00A0]), 'NBSP a NBSP.Trim([NBSP])');
  CheckEqS('a' + #$00A0, N.TrimLeft([#$00A0]), 'NBSP a NBSP.TrimLeft([NBSP])');
  CheckEqS(#$00A0 + 'a', N.TrimRight([#$00A0]), 'NBSP a NBSP.TrimRight([NBSP])');
  V := 'aaa';
  CheckEqS('', V.Trim(['a']), 'aaa.Trim([a])');
  CheckEqS('', V.TrimLeft(['a']), 'aaa.TrimLeft([a])');
  CheckEqS('', V.TrimRight(['a']), 'aaa.TrimRight([a])');
  CheckEqS('', E.Trim, 'E.Trim');
  CheckEqS('', E.TrimLeft, 'E.TrimLeft');
  CheckEqS('', E.TrimRight, 'E.TrimRight');
  CheckEqS('', E.Trim(['x']), 'E.Trim([x])');
  CheckEqS('', E.TrimLeft(['x']), 'E.TrimLeft([x])');
  CheckEqS('', E.TrimRight(['x']), 'E.TrimRight([x])');
  CheckEqS(U, U.Trim, 'U.Trim');
  CheckEqS('Gr' + #$00FC + #$00DF + 'e', U.TrimRight([' ', #$0438, #$0416]), 'U.TrimRight([space, i, Zhe])');
  CheckEqS(#$D83D + #$DE00 + 'b', P.TrimLeft(['a']), 'P.TrimLeft([a])');
  CheckEqS('a' + #$D83D, P.TrimRight(['b', #$DE00]), 'P.TrimRight([b, low surrogate]) cuts the pair');

  Section('Pad');
  CheckEqS('  abc', S.PadLeft(5), 'abc.PadLeft(5)');
  CheckEqS('**abc', S.PadLeft(5, '*'), 'abc.PadLeft(5, *)');
  CheckEqS('abc', S.PadLeft(3), 'abc.PadLeft(3)');
  CheckEqS('abc', S.PadLeft(2), 'abc.PadLeft(2)');
  CheckEqS('abc', S.PadLeft(-1), 'abc.PadLeft(-1)');
  CheckEqS('abc  ', S.PadRight(5), 'abc.PadRight(5)');
  CheckEqS('abc..', S.PadRight(5, '.'), 'abc.PadRight(5, .)');
  CheckEqS('abc', S.PadRight(3), 'abc.PadRight(3)');
  CheckEqS('abc', S.PadRight(-1, '.'), 'abc.PadRight(-1, .)');
  CheckEqS('00', E.PadLeft(2, '0'), 'E.PadLeft(2, 0)');
  CheckEqS('', E.PadLeft(0), 'E.PadLeft(0)');
  CheckEqS('  ', E.PadRight(2), 'E.PadRight(2)');
  CheckEqS('-' + P, P.PadLeft(5, '-'), 'P.PadLeft(5, -)');
  CheckEqS(P + #$00B7, P.PadRight(5, #$00B7), 'P.PadRight(5, middle dot)');
  CheckEqS(U, U.PadLeft(8), 'U.PadLeft(8)');
  CheckEqS('abc', S, 'abc is unchanged');
  Done;
end.
