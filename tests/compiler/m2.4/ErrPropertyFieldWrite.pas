// covers: LANG-350, LANG-641
program ErrPropertyFieldWrite;

{ A write through a property whose write accessor is a field is checked like the field written directly (C-454):
  a const parameter, a typed constant, a for-in variable, a read-only with target and nested properties give E2053;
  a write accessor that is a method of the record stays allowed on a const value. }

type
  TInner = record
    FX: Integer;
    property PX: Integer read FX write FX;
  end;

  TRec = record
    FX: Integer;
    FInner: TInner;
    procedure SetM(V: Integer);
    property PX: Integer read FX write FX;
    property Inner: TInner read FInner write FInner;
    property PM: Integer read FX write SetM;
  end;

procedure TRec.SetM(V: Integer);
begin
end;

const
  Origin: TRec = (FX: 1);

procedure Change(const R: TRec; const List: array of TRec);
var
  Item: TRec;
begin
  R.PX := 5;
  Origin.PX := 5;
  for Item in List do
    Item.PX := 5;
  with R do
    PX := 5;
  R.Inner.PX := 5;
  R.PM := 5;
end;

begin
end.
