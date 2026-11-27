// covers: LANG-276, LANG-277, CMP-1417, CMP-1418
program ArrayAppendLoop;

{ A := A + [I] in a loop: one _DynArrayCat per statement with A as the destination and its first source, so the run
  time appends in place while the block's room lasts (amortised growth): 100 000 appends stay well within the time
  limit, and the block moves only on the few reallocations. The same for an array of strings, and for
  Insert(I, B, Length(B)), which grows the same way. }

type
  TInts = array of Integer;
  TStrs = array of string;

var
  A, B: TInts;
  S: TStrs;
  I, Moves: Integer;
  Sum: Int64;
  Last: Pointer;

begin
  Moves := 0;
  Last := nil;
  for I := 0 to 99999 do
  begin
    A := A + [I];
    if Pointer(A) <> Last then
    begin
      Inc(Moves);
      Last := Pointer(A);
    end;
  end;
  Sum := 0;
  for I := 0 to High(A) do
    Sum := Sum + A[I];
  Writeln('Length ', Length(A), ' sum ', Sum);
  Writeln('block moved fewer than 100 times: ', Moves < 100);
  for I := 1 to 20000 do
    S := S + ['s' + Chr(Ord('a') + I mod 26)];
  Writeln('strings ', Length(S), ' ', S[0], ' ', S[19999]);
  Moves := 0;
  Last := nil;
  for I := 0 to 99999 do
  begin
    Insert(I, B, Length(B));
    if Pointer(B) <> Last then
    begin
      Inc(Moves);
      Last := Pointer(B);
    end;
  end;
  Sum := 0;
  for I := 0 to High(B) do
    Sum := Sum + B[I];
  Writeln('Insert: Length ', Length(B), ' sum ', Sum);
  Writeln('Insert: block moved fewer than 100 times: ', Moves < 100);
end.
