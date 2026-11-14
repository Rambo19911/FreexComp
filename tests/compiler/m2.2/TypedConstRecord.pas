// covers: LANG-344, LANG-522, CMP-341
program TypedConstRecord;
type
  TPoint = record
    X, Y: Integer;
  end;
  TColor = (Red, Green, Blue);
  TStyle = record
    Color: TColor;
    Width: Byte;
    Dashed: Boolean;
    Origin: TPoint;
    Scale: Int64;
  end;
  TItem = record
    Code: Char;
    Qty: Word;
  end;
const
  Origin: TPoint = (X: 0; Y: 0);
  Corner: TPoint = (X: -5; Y: 12);
  Style: TStyle = (Color: Blue; Width: 3; Dashed: True; Origin: (X: 7; Y: 8); Scale: 10000000000);
  Partial: TStyle = (Color: Green; Width: 1);
  Empty: TPoint = ();
  Items: array[1..3] of TItem = ((Code: 'a'; Qty: 1), (Code: 'b'; Qty: 20), (Code: 'c'; Qty: 300));
  Small: packed record
    B: Byte;
    I: Integer;
  end = (B: 255; I: -1);
var
  P: TPoint = (X: 1; Y: 2);
  Q: TPoint;
  I, Total: Integer;
begin
  Writeln(Origin.X, ' ', Origin.Y, ' ', Corner.X, ' ', Corner.Y);
  Writeln(Ord(Style.Color), ' ', Style.Width, ' ', Style.Dashed, ' ', Style.Origin.X, ' ', Style.Origin.Y, ' ', Style.Scale);
  Writeln(Ord(Partial.Color), ' ', Partial.Width, ' ', Partial.Dashed, ' ', Partial.Origin.Y, ' ', Partial.Scale);
  Writeln(Empty.X, ' ', Empty.Y);
  Total := 0;
  for I := 1 to 3 do
  begin
    Write(Items[I].Code);
    Total := Total + Items[I].Qty;
  end;
  Writeln(' ', Total);
  Writeln(Small.B, ' ', Small.I, ' ', SizeOf(Small));
  Q := Corner;
  P.X := P.X + Q.X;
  Writeln(P.X, ' ', P.Y, ' ', Q.Y);
end.
