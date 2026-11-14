// covers: LANG-208, LANG-268, CMP-340
program NewDispose;
type
  PNode = ^TNode;
  TNode = record
    Value: Integer;
    Next: PNode;
  end;
  TEmpty = record
  end;
  PEmpty = ^TEmpty;
  TBig = record
    A: array[0..99] of Integer;
  end;
  PBig = ^TBig;
var
  Head, N, Prev: PNode;
  I, Sum, Count: Integer;
  E1, E2: PEmpty;
  V1, V2: TEmpty;
  B: PBig;
  AllZero: Boolean;
begin
  Head := nil;
  for I := 1 to 5 do
  begin
    New(N);
    N^.Value := I * I;
    N^.Next := Head;
    Head := N;
  end;
  Sum := 0;
  Count := 0;
  N := Head;
  while N <> nil do
  begin
    Write(N^.Value, ' ');
    Sum := Sum + N.Value;
    Inc(Count);
    N := N^.Next;
  end;
  Writeln('|');
  Writeln(Count, ' ', Sum);
  Prev := nil;
  while Head <> nil do
  begin
    N := Head^.Next;
    Head^.Next := Prev;
    Prev := Head;
    Head := N;
  end;
  Head := Prev;
  N := Head;
  while N <> nil do
  begin
    Write(N^.Value, ' ');
    N := N^.Next;
  end;
  Writeln('|');
  while Head <> nil do
  begin
    N := Head;
    Head := Head^.Next;
    Dispose(N);
  end;
  Writeln(Head = nil);
  // New zero-fills
  New(B);
  AllZero := True;
  for I := 0 to 99 do
    if B^.A[I] <> 0 then
      AllZero := False;
  Writeln(AllZero, ' ', SizeOf(TBig));
  Dispose(B);
  // an empty record has size 0 but occupies one byte when allocated (ABI-060)
  New(E1);
  New(E2);
  Writeln(SizeOf(TEmpty), ' ', E1 <> nil, ' ', E1 <> E2, ' ', @V1 <> @V2);
  Dispose(E1);
  Dispose(E2);
end.
