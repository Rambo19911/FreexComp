// covers: LANG-255, LANG-260, CMP-307, CMP-308, CMP-326
program ArrayOfRecord;
type
  TItem = record
    Id: Integer;
    Qty: Word;
    Tag: Char;
  end;
  TStock = record
    Count: Integer;
    Items: array[1..4] of TItem;
  end;
  TGrid = array[0..1, 0..2] of TItem;
var
  S, T: TStock;
  G: TGrid;
  I, J: Integer;
  Total: Integer;
begin
  S.Count := 0;
  for I := 1 to 4 do
  begin
    S.Items[I].Id := I * 10;
    S.Items[I].Qty := I * I;
    S.Items[I].Tag := Chr(Ord('a') + I - 1);
    Inc(S.Count);
  end;
  T := S;
  T.Items[2].Qty := 99;
  T.Items[3] := T.Items[1];
  Total := 0;
  for I := 1 to S.Count do
    Total := Total + S.Items[I].Qty;
  Writeln(S.Count, ' ', Total, ' ', SizeOf(TItem), ' ', SizeOf(TStock));
  for I := 1 to 4 do
    Write(T.Items[I].Id, ':', T.Items[I].Qty, T.Items[I].Tag, ' ');
  Writeln('|');
  Writeln(S.Items[2].Qty, ' ', S.Items[3].Tag, ' ', Length(S.Items), ' ', High(T.Items));
  for I := 0 to 1 do
    for J := 0 to 2 do
    begin
      G[I, J].Id := I * 3 + J;
      G[I][J].Tag := 'x';
    end;
  G[1] := G[0];
  G[0, 2].Id := 77;
  Writeln(G[0, 0].Id, ' ', G[0, 2].Id, ' ', G[1, 2].Id, ' ', G[1][1].Tag, ' ', SizeOf(G));
end.
