// covers: LANG-770, LANG-730
program NestedTypes;

type
  TOuter = class
  public
    type
      TKind = (kNone, kSmall, kLarge);
      TInner = record
        A, B: Integer;
        function Total: Integer;
      end;
    const
      Limit = 3;
      Title = 'outer';
  private
    FKind: TKind;
  public
    Data: TInner;
    procedure SetKind(K: TKind);
    function Describe: string;
  end;

  TBox = record
  const
    Size = 4;
  type
    TCells = array[1..Size] of Integer;
  var
    Cells: TCells;
    function Sum: Integer;
  end;

function TOuter.TInner.Total: Integer;
begin
  Result := A + B;
end;

procedure TOuter.SetKind(K: TKind);
begin
  FKind := K;
end;

function TOuter.Describe: string;
begin
  if FKind = kLarge then
    Result := Title + ' large'
  else
    Result := Title + ' other';
end;

function TBox.Sum: Integer;
var
  I: Integer;
begin
  Result := 0;
  for I := 1 to Size do
    Result := Result + Cells[I];
end;

var
  O: TOuter;
  Inner: TOuter.TInner;
  Kind: TOuter.TKind;
  Box: TBox;
  I: Integer;
begin
  O := TOuter.Create;
  O.Data.A := 2;
  O.Data.B := 5;
  Writeln(O.Data.Total);
  Inner.A := 10;
  Inner.B := TOuter.Limit;
  Writeln(Inner.Total);
  Kind := TOuter.kLarge;
  O.SetKind(Kind);
  Writeln(O.Describe);
  O.SetKind(TOuter.kSmall);
  Writeln(O.Describe, ' ', Ord(TOuter.kSmall), ' ', TOuter.Title);
  for I := 1 to TBox.Size do
    Box.Cells[I] := I * I;
  Writeln(Box.Sum, ' ', SizeOf(TBox.TCells));
  O.Free;
end.
