// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
{ Types - geometry records, dynamic-array types and value relations (other-units.md section 1, RTL-600).
  TPoint, TRect and TSize have the layout of the Win32 POINT, RECT and SIZE structures. Uses only System.
  TRect is declared without its TopLeft/BottomRight variant until variant records exist (M2.5). }
unit Types;

interface

type
  PPoint = ^TPoint;
  TPoint = record
    X, Y: Integer;
  end;

  PSize = ^TSize;
  TSize = record
    cx, cy: Integer;
  end;

  PRect = ^TRect;
  TRect = record
    Left, Top, Right, Bottom: Integer;
  end;

  TSmallPoint = record
    x, y: SmallInt;
  end;

  TPointF = record
    X, Y: Single;
  end;

  TRectF = record
    Left, Top, Right, Bottom: Single;
  end;

  TIntegerDynArray = array of Integer;
  TStringDynArray = array of string;
  TByteDynArray = array of Byte;
  TDoubleDynArray = array of Double;

  TValueRelationship = -1..1;
  TValueSign = -1..1;

const
  LessThanValue = -1;
  EqualsValue = 0;
  GreaterThanValue = 1;

function Point(X, Y: Integer): TPoint;
function Rect(Left, Top, Right, Bottom: Integer): TRect;
function Bounds(Left, Top, Width, Height: Integer): TRect;
function Size(cx, cy: Integer): TSize;
function SmallPoint(X, Y: SmallInt): TSmallPoint;
function RectWidth(const R: TRect): Integer;
function RectHeight(const R: TRect): Integer;
function IsRectEmpty(const R: TRect): Boolean;
function EqualRect(const R1, R2: TRect): Boolean;
function PtInRect(const R: TRect; const P: TPoint): Boolean;
function PointsEqual(const P1, P2: TPoint): Boolean;
function CenterPoint(const R: TRect): TPoint;
function OffsetRect(var R: TRect; DX, DY: Integer): Boolean;
function InflateRect(var R: TRect; DX, DY: Integer): Boolean;
{ R may be the same variable as R1 or R2 }
function IntersectRect(out R: TRect; const R1, R2: TRect): Boolean;
function UnionRect(out R: TRect; const R1, R2: TRect): Boolean;

implementation

function Point(X, Y: Integer): TPoint;
begin
  Result.X := X;
  Result.Y := Y;
end;

function Rect(Left, Top, Right, Bottom: Integer): TRect;
begin
  Result.Left := Left;
  Result.Top := Top;
  Result.Right := Right;
  Result.Bottom := Bottom;
end;

function Bounds(Left, Top, Width, Height: Integer): TRect;
begin
  Result := Rect(Left, Top, Left + Width, Top + Height);
end;

function Size(cx, cy: Integer): TSize;
begin
  Result.cx := cx;
  Result.cy := cy;
end;

function SmallPoint(X, Y: SmallInt): TSmallPoint;
begin
  Result.x := X;
  Result.y := Y;
end;

function RectWidth(const R: TRect): Integer;
begin
  Result := R.Right - R.Left;
end;

function RectHeight(const R: TRect): Integer;
begin
  Result := R.Bottom - R.Top;
end;

function IsRectEmpty(const R: TRect): Boolean;
begin
  Result := (R.Right <= R.Left) or (R.Bottom <= R.Top);
end;

function EqualRect(const R1, R2: TRect): Boolean;
begin
  Result := (R1.Left = R2.Left) and (R1.Top = R2.Top) and (R1.Right = R2.Right) and (R1.Bottom = R2.Bottom);
end;

function PtInRect(const R: TRect; const P: TPoint): Boolean;
begin
  Result := (P.X >= R.Left) and (P.X < R.Right) and (P.Y >= R.Top) and (P.Y < R.Bottom);
end;

function PointsEqual(const P1, P2: TPoint): Boolean;
begin
  Result := (P1.X = P2.X) and (P1.Y = P2.Y);
end;

function CenterPoint(const R: TRect): TPoint;
begin
  Result.X := R.Left + (R.Right - R.Left) div 2;
  Result.Y := R.Top + (R.Bottom - R.Top) div 2;
end;

function OffsetRect(var R: TRect; DX, DY: Integer): Boolean;
begin
  R.Left := R.Left + DX;
  R.Top := R.Top + DY;
  R.Right := R.Right + DX;
  R.Bottom := R.Bottom + DY;
  Result := True;
end;

function InflateRect(var R: TRect; DX, DY: Integer): Boolean;
begin
  R.Left := R.Left - DX;
  R.Top := R.Top - DY;
  R.Right := R.Right + DX;
  R.Bottom := R.Bottom + DY;
  Result := True;
end;

function Max(A, B: Integer): Integer;
begin
  if A > B then
    Result := A
  else
    Result := B;
end;

function Min(A, B: Integer): Integer;
begin
  if A < B then
    Result := A
  else
    Result := B;
end;

function IntersectRect(out R: TRect; const R1, R2: TRect): Boolean;
var
  T: TRect;
begin
  T.Left := Max(R1.Left, R2.Left);
  T.Top := Max(R1.Top, R2.Top);
  T.Right := Min(R1.Right, R2.Right);
  T.Bottom := Min(R1.Bottom, R2.Bottom);
  Result := not IsRectEmpty(T);
  if Result then
    R := T
  else
    R := Rect(0, 0, 0, 0);
end;

function UnionRect(out R: TRect; const R1, R2: TRect): Boolean;
var
  T: TRect;
begin
  if IsRectEmpty(R1) then
  begin
    if IsRectEmpty(R2) then
      T := Rect(0, 0, 0, 0)
    else
      T := R2;
  end
  else if IsRectEmpty(R2) then
    T := R1
  else
  begin
    T.Left := Min(R1.Left, R2.Left);
    T.Top := Min(R1.Top, R2.Top);
    T.Right := Max(R1.Right, R2.Right);
    T.Bottom := Max(R1.Bottom, R2.Bottom);
  end;
  Result := not IsRectEmpty(T);
  R := T;
end;

end.
