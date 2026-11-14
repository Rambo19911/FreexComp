// covers: LANG-260, CMP-306, CMP-307, CMP-326
program RecordBasics;
type
  TPoint = record
    X, Y: Integer;
  end;
  TRect = record
    TopLeft, BottomRight: TPoint;
    Name: Char;
  end;
  TColor = (Red, Green, Blue);
  TPixel = record
    Pos: TPoint;
    Color: TColor;
    Visible: Boolean;
    Weight: Int64;
  end;
var
  P, Q: TPoint;
  R, S: TRect;
  Px: TPixel;
  Anon: record
    A: Byte;
    B: Word;
  end;

procedure Show(var P: TPoint);
begin
  Writeln('(', P.X, ', ', P.Y, ')');
end;

procedure Move(var P: TPoint; DX, DY: Integer);
begin
  P.X := P.X + DX;
  P.Y := P.Y + DY;
end;

function Area(var R: TRect): Integer;
begin
  Result := (R.BottomRight.X - R.TopLeft.X) * (R.BottomRight.Y - R.TopLeft.Y);
end;

procedure Swap(var A, B: TPoint);
var
  T: TPoint;
begin
  T := A;
  A := B;
  B := T;
end;

procedure Outer;
var
  L: TPoint;

  procedure Twice;
  begin
    L.X := L.X * 2;
  end;

begin
  L.X := 21;
  L.Y := 0;
  Twice;
  Show(L);
end;

begin
  P.X := 1;
  P.Y := 2;
  Q := P;
  Q.X := 10;
  Show(P);
  Show(Q);
  Move(P, 5, -7);
  Show(P);
  R.TopLeft := P;
  R.BottomRight.X := 16;
  R.BottomRight.Y := 15;
  R.Name := 'r';
  S := R;
  S.TopLeft.X := 0;
  S.Name := 's';
  Show(R.TopLeft);
  Show(S.TopLeft);
  Writeln(R.Name, S.Name, ' ', Area(R), ' ', Area(S));
  Px.Pos := S.BottomRight;
  Px.Color := Blue;
  Px.Visible := True;
  Px.Weight := 5000000000;
  Writeln(Px.Pos.X, ' ', Px.Pos.Y, ' ', Ord(Px.Color), ' ', Px.Visible, ' ', Px.Weight);
  Anon.A := 200;
  Anon.B := Anon.A * 300;
  Writeln(Anon.A, ' ', Anon.B);
  Swap(P, Q);
  Show(P);
  Show(Q);
  Outer;
end.
