// covers: LANG-615, LANG-635, ABI-072
program ClassManagedFields;

type
  TInts = array of Integer;

  TBase = class
    Title: string;
    Values: TInts;
  end;

  TItem = class(TBase)
    Count: Integer;
    Tags: array of string;
    Note: string;
    procedure Fill(N: Integer);
  end;

procedure TItem.Fill(N: Integer);
var
  I: Integer;
begin
  SetLength(Values, N);
  SetLength(Tags, N);
  for I := 0 to N - 1 do
  begin
    Values[I] := I * I;
    Tags[I] := 'tag' + Chr(Ord('A') + I);
  end;
  Count := N;
  Note := Title + '!';
end;

var
  Item: TItem;
  Before: NativeInt;
  I: Integer;
begin
  Before := _LiveAllocCount;
  Item := TItem.Create;
  Writeln(Item.Title = '', ' ', Length(Item.Values), ' ', Item.Count, ' ', Length(Item.Tags));
  Item.Title := 'item';
  Item.Title := Item.Title + 's';
  Item.Fill(3);
  for I := 0 to Item.Count - 1 do
    Write(Item.Values[I], ' ', Item.Tags[I], ' ');
  Writeln(Item.Note);
  Item.Free;
  Writeln('leaked: ', _LiveAllocCount - Before);
end.
