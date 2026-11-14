// covers: RTL-027
// opc-run: variants 1 2 3
program Sys_Sets;
{ Big sets (256 bits, rtl-abi.md ABI-050, helpers of section 9.8): every binary operator, comparison and variable range
  goes through a System helper; the operands are held in variables so nothing is folded at compile time.
  Variant 1: the operators. Variant 2: a variable range outside 0..255 under $R+ is error 201. Variant 3: under $R-
  the same range is clipped by the compiler and adds only the elements inside 0..255. }

type
  TByteSet = set of Byte;
  TCharSet = set of Char;
  TRaw = array[0..31] of Byte;

// TODO(C-072): call System._SetSymDiff directly; a program's call to a System routine is emitted without its
// "declare" today, so the link name is declared here and resolved by the linker.
procedure SetSymDiff(Dest, A, B: Pointer); external name 'System._SetSymDiff';

var
  A, B, C, E: TByteSet;
  Cs: TCharSet;
  Lo, Hi, K: Integer;
  P: PChar;

{ elements as a list of runs: 1..3 7 250..255 }
procedure Dump(const S: TByteSet);
var
  I, First: Integer;
  Any: Boolean;
begin
  Write(': [');
  Any := False;
  I := 0;
  while I <= 255 do
  begin
    if I in S then
    begin
      First := I;
      while (I < 255) and ((I + 1) in S) do
        I := I + 1;
      if Any then
        Write(' ');
      Any := True;
      if First = I then
        Write(I)
      else
        Write(First, '..', I);
    end;
    I := I + 1;
  end;
  Writeln(']');
end;

{ ABI-050: bit k is bit k mod 8 of byte k div 8 }
procedure DumpRaw(const S: TByteSet);
var
  R: TRaw;
  I: Integer;
begin
  R := TRaw(S);
  for I := 0 to 31 do
    Write(' ', R[I]);
  Writeln;
end;

procedure Operators;
var
  J: Integer;
begin
  Lo := 10;
  Hi := 20;
  A := [Lo..Hi, 0, 255];
  Lo := 15;
  Hi := 130;
  B := [Lo..Hi];
  E := [];
  Write('A'); Dump(A);
  Write('B'); Dump(B);
  C := A + B;
  Write('A+B'); Dump(C);
  C := A * B;
  Write('A*B'); Dump(C);
  C := A - B;
  Write('A-B'); Dump(C);
  C := B - A;
  Write('B-A'); Dump(C);
  SetSymDiff(@C, @A, @B);             { no operator reaches it (the language has no symmetric difference) }
  Write('A><B'); Dump(C);
  C := A + E;
  Write('A+[]'); Dump(C);
  C := A * E;
  Write('A*[]'); Dump(C);
  C := A;
  C := C + C;                          { destination equal to both operands }
  Write('C+C'); Dump(C);
  C := C - C;
  Write('C-C'); Dump(C);

  Writeln('A=A ', A = A, ' A=B ', A = B, ' A<>B ', A <> B, ' []=[] ', E = []);
  C := A * B;
  Writeln('A*B<=A ', C <= A, ' A*B<=B ', C <= B, ' A<=B ', A <= B, ' A>=A*B ', A >= C, ' B>=A ', B >= A);
  Writeln('[]<=A ', E <= A, ' A<=[] ', A <= E, ' A<=A ', A <= A);
  C := A;
  Include(C, 128);
  Writeln('A<=A+[128] ', A <= C, ' A+[128]<=A ', C <= A, ' equal ', A = C);

  K := 255;
  Writeln('255 in A ', K in A, ' 254 in A ', (K - 1) in A, ' 300 in A ', (K + 45) in A, ' -1 in A ', (K - 256) in A);

  Lo := 0;
  Hi := 255;
  C := [Lo..Hi];
  Write('full'); Dump(C);
  Write('full:'); DumpRaw(C);
  Lo := 5;
  Hi := 4;
  C := [Lo..Hi];
  Write('5..4'); Dump(C);
  Lo := 7;
  Hi := 7;
  C := [1, Lo..Hi, 200];
  Write('7..7'); Dump(C);
  Lo := 8;
  Hi := 16;
  C := [Lo..Hi, 255];
  Write('8..16,255:'); DumpRaw(C);
  C := C - [Lo..Hi];
  Write('255:'); DumpRaw(C);

  Cs := ['a'..'e', 'x'];
  Lo := Ord('c');
  Hi := Ord('z');
  Cs := Cs * [Char(Lo)..Char(Hi)];
  Write('chars:');
  for J := 0 to 255 do
    if Char(J) in Cs then
      Write(' ', Char(J));
  Writeln;
end;

procedure RangeChecked;
begin
{$R+}
  Lo := 250;
  Hi := 256;
  C := [Lo..Hi];
{$R-}
  Write('not reached'); Dump(C);
end;

procedure RangeClipped;
begin
{$R-}
  Lo := -3;
  Hi := 2;
  C := [Lo..Hi];
  Write('-3..2'); Dump(C);
  Lo := 254;
  Hi := 400;
  C := [Lo..Hi];
  Write('254..400'); Dump(C);
  Lo := 300;
  Hi := 400;
  C := [Lo..Hi];
  Write('300..400'); Dump(C);
  Lo := -9;
  Hi := -2;
  C := [Lo..Hi];
  Write('-9..-2'); Dump(C);
end;

begin
  P := CmdLine;                        { the variant is the last character of the command line (ParamStr: R-011) }
  while P^ <> #0 do
    Inc(P);
  Dec(P);
  case P^ of
    '1': Operators;
    '2': RangeChecked;
    '3': RangeClipped;
  end;
end.
