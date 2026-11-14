// covers: CMP-325, ABI-031, ABI-080, ABI-110, ABI-140, LANG-260
program StringInRecordArray;
// Managed fields and elements: record and static-array assignment (_CopyRecord), value parameters (a caller copy for large
// records, the callee's own reference for 8-byte ones), out parameters, managed results, nested records, static arrays of
// strings and of records, dynamic arrays of managed records, New/Dispose; the RTL leak counter is unchanged at the end.
type
  TPerson = record
    Name: string;
    Age: Integer;
    Tags: array of string;
  end;
  TPair = record
    Key: AnsiString;
    Value: TPerson;
  end;
  TNames = array[1..3] of string;
  TPeople = array of TPerson;
  PPerson = ^TPerson;
  TSmall = record
    S: string;
  end;

procedure Show(const P: TPerson);
var
  K: Integer;
begin
  Write(P.Name, ' ', P.Age, ' [');
  for K := 0 to High(P.Tags) do
    Write(P.Tags[K], ';');
  Writeln(']');
end;

function MakePerson(const N: string; A: Integer): TPerson;
begin
  Result.Name := N;
  Result.Age := A;
  SetLength(Result.Tags, 1);
  Result.Tags[0] := N + '!';
end;

procedure Mutate(P: TPerson);
begin
  P.Name := P.Name + '*';
  P.Age := P.Age + 1;
  P.Tags[0] := 'shared';
  Show(P);
end;

procedure Rename(var P: TPerson; const N: string);
begin
  P.Name := N;
end;

procedure Wipe(out P: TPerson);
begin
  P.Age := -1;
end;

function Twice(const S: string): TSmall;
begin
  Result.S := S + S;
end;

procedure ShowSmall(X: TSmall);
begin
  X.S := X.S + '?';
  Writeln(X.S);
end;

procedure Scenario;
var
  A, B: TPerson;
  Pair, Pair2: TPair;
  Names, Names2: TNames;
  Team: array[1..2] of TPerson;
  People: TPeople;
  P: PPerson;
  Grid: array[0..1, 0..1] of string;
  Sm: TSmall;
  I: Integer;
begin
  A := MakePerson('Ann', 30);
  B := A;
  B.Name := 'Bob';
  B.Age := 40;
  Show(A);
  Show(B);
  Mutate(A);
  Show(A);
  Rename(B, 'Bea');
  Show(B);
  Pair.Key := 'k1';
  Pair.Value := B;
  Pair2 := Pair;
  Pair2.Key := Pair2.Key + 'x';
  Pair2.Value.Name := 'Cy';
  Writeln(Pair.Key, ' ', Pair.Value.Name, ' ', Pair2.Key, ' ', Pair2.Value.Name);
  Names[1] := 'one';
  Names[2] := A.Name;
  Names[3] := Names[1] + Names[2];
  Names2 := Names;
  Names2[1] := 'uno';
  Writeln(Names[1], ' ', Names[3], ' ', Names2[1], ' ', Names2[3]);
  Team[1] := A;
  Team[2] := MakePerson('Dan', 5);
  SetLength(People, 2);
  People[0] := Team[2];
  People[0].Tags[0] := 'x';
  SetLength(People, 3);
  People[2].Name := 'Eve';
  for I := 0 to High(People) do
    Show(People[I]);
  Show(Team[2]);
  SetLength(People, 1);
  New(P);
  P^ := People[0];
  P^.Name := 'Fay';
  Show(P^);
  Dispose(P);
  Wipe(B);
  Show(B);
  Grid[0, 0] := 'a';
  Grid[1, 1] := Grid[0, 0] + 'b';
  Writeln(Grid[0, 0], '|', Grid[0, 1], '|', Grid[1, 1]);
  Sm := Twice('ab');
  ShowSmall(Sm);
  Writeln(Sm.S);
  for I := 1 to 3 do
    A := MakePerson(Names[I], I);
  Show(A);
end;

var
  Before: NativeInt;
begin
  Before := _LiveAllocCount;
  Scenario;
  Writeln('leaked ', _LiveAllocCount - Before);
end.
