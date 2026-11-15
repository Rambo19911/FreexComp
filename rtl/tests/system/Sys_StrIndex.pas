// covers: RTL-077, ABI-102
// opc-run: variants 1 2 3 4
program Sys_StrIndex;
{ String indexing S[i] (system.md section 8.1). The variant is ParamStr(1).
  1: with range checking off, valid indices read and write the characters, and S[Length(S)+1] reads the
     terminator #0 of a non-empty string (also for an AnsiString).
  2: with range checking on, i = 0 is runtime error 201.
  3: with range checking on, i = Length(S) + 1 is runtime error 201 (reading).
  4: with range checking on, writing S[i] with i > Length(S) is runtime error 201. }
uses TestKit;

var
  S: string;
  A: AnsiString;
  I: Integer;

{$R-}
procedure Unchecked;
begin
  S := 'abc';
  I := Length(S) + 1;
  CheckEqI(Ord('a'), Ord(S[1]), 'S[1]');
  CheckEqI(Ord('c'), Ord(S[3]), 'S[Length(S)]');
  CheckEqI(0, Ord(S[I]), 'S[Length(S)+1] is the terminator');
  S[2] := 'X';
  CheckEqS('aXc', S, 'write S[2]');
  A := 'de';
  I := Length(A) + 1;
  CheckEqI(0, Ord(A[I]), 'AnsiString A[Length(A)+1] is the terminator');
  Done;
end;

{$R+}
procedure Checked(Variant: Char);
var
  C: Char;
begin
  S := 'abc';
  C := S[3];
  Writeln('S[3] = ', C);
  case Variant of
    '2':
      begin
        I := 0;
        C := S[I];
      end;
    '3':
      begin
        I := Length(S) + 1;
        C := S[I];
      end;
    '4':
      begin
        I := 9;
        S[I] := 'z';
      end;
  end;
  Writeln('not reached ', C);
end;

begin
  if ParamStr(1) = '1' then
    Unchecked
  else
    Checked(ParamStr(1)[1]);
end.
