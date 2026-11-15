// covers: LANG-280, LANG-290
program ClassToPointer;

type
  TItem = class
    FValue: Integer;
  end;
  TBigItem = class(TItem)
  end;
  TItemClass = class of TItem;

var
  Slots: array[0..3] of Pointer;
  Count: Integer;

procedure Add(P: Pointer);
begin
  Slots[Count] := P;
  Count := Count + 1;
end;

function IndexOf(const P: Pointer): Integer;
var
  I: Integer;
begin
  Result := -1;
  for I := 0 to Count - 1 do
    if Slots[I] = P then
      Result := I;
end;

var
  A: TItem;
  B: TBigItem;
  C: TItemClass;
  P: Pointer;
  Before: NativeInt;
begin
  Before := _LiveAllocCount;
  A := TItem.Create;
  A.FValue := 7;
  B := TBigItem.Create;
  B.FValue := 9;
  Add(A);
  Add(B);
  C := TBigItem;
  Add(C);
  Add(TItem);
  Writeln(Count, ' ', IndexOf(B), ' ', IndexOf(A), ' ', IndexOf(TItem), ' ', IndexOf(C));
  P := A;
  Writeln(P = A, ' ', A <> P, ' ', P = B, ' ', TItem(P).FValue);
  P := B;
  Writeln(TItem(Slots[1]).FValue, ' ', P = Slots[1], ' ', TItemClass(Slots[2]) = TBigItem);
  P := nil;
  Writeln(P = A);
  A.Free;
  B.Free;
  Writeln('leaked: ', _LiveAllocCount - Before);
end.
