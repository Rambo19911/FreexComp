// covers: RTL-600
program Types_Rects;
{ Types unit (other-units.md section 1): records, constants and every function of the table with its exact result,
  including negative and empty rectangles. }
uses Types, TestKit;

procedure CheckRect(const R: TRect; L, T, Rt, B: Integer; const Name: string);
begin
  Check((R.Left = L) and (R.Top = T) and (R.Right = Rt) and (R.Bottom = B), Name);
end;

var
  R, R2: TRect;
  P: TPoint;
  S: TSize;
  SP: TSmallPoint;
  PF: TPointF;
  RF: TRectF;
  Ints: TIntegerDynArray;
  Strs: TStringDynArray;
  Bytes: TByteDynArray;
  Dbls: TDoubleDynArray;
  Rel: TValueRelationship;
  Sign: TValueSign;
  PP: PPoint;
  PR: PRect;
  PS: PSize;

begin
  Section('records and constants');
  CheckEqI(8, SizeOf(TPoint), 'SizeOf TPoint');
  CheckEqI(16, SizeOf(TRect), 'SizeOf TRect');
  CheckEqI(8, SizeOf(TSize), 'SizeOf TSize');
  CheckEqI(4, SizeOf(TSmallPoint), 'SizeOf TSmallPoint');
  CheckEqI(8, SizeOf(TPointF), 'SizeOf TPointF');
  CheckEqI(16, SizeOf(TRectF), 'SizeOf TRectF');
  CheckEqI(-1, LessThanValue, 'LessThanValue');
  CheckEqI(0, EqualsValue, 'EqualsValue');
  CheckEqI(1, GreaterThanValue, 'GreaterThanValue');
  Rel := LessThanValue;
  Sign := 1;
  CheckEqI(-1, Rel, 'TValueRelationship');
  CheckEqI(1, Sign, 'TValueSign');
  SetLength(Ints, 2); SetLength(Strs, 1); SetLength(Bytes, 3); SetLength(Dbls, 4);
  Strs[0] := 'x';
  CheckEqI(10, Length(Ints) + Length(Strs) + Length(Bytes) + Length(Dbls), 'dynamic array types');
  PF.X := 1.5; PF.Y := -2;
  RF.Left := 0; RF.Top := 0; RF.Right := 0.25; RF.Bottom := 1;
  Check((PF.X + PF.Y = -0.5) and (RF.Right = 0.25), 'TPointF, TRectF');

  Section('constructors');
  P := Point(3, -4);
  Check((P.X = 3) and (P.Y = -4), 'Point');
  CheckRect(Rect(1, 2, 3, 4), 1, 2, 3, 4, 'Rect');
  CheckRect(Bounds(10, 20, 30, 40), 10, 20, 40, 60, 'Bounds');
  CheckRect(Bounds(10, 20, -5, -5), 10, 20, 5, 15, 'Bounds negative size');
  S := Size(7, 8);
  Check((S.cx = 7) and (S.cy = 8), 'Size');
  SP := SmallPoint(-32768, 32767);
  Check((SP.x = -32768) and (SP.y = 32767), 'SmallPoint');
  PP := @P; PR := @R; PS := @S;
  Check((PP^.X = 3) and (PS^.cy = 8) and (PR = @R), 'PPoint, PRect, PSize');

  Section('measures and tests');
  R := Rect(10, 20, 4, 50);
  CheckEqI(-6, RectWidth(R), 'RectWidth negative');
  CheckEqI(30, RectHeight(R), 'RectHeight');
  CheckEqB(True, IsRectEmpty(R), 'IsRectEmpty right <= left');
  CheckEqB(True, IsRectEmpty(Rect(0, 0, 5, 0)), 'IsRectEmpty zero height');
  CheckEqB(False, IsRectEmpty(Rect(0, 0, 1, 1)), 'IsRectEmpty 1x1');
  CheckEqB(True, EqualRect(Rect(1, 2, 3, 4), Rect(1, 2, 3, 4)), 'EqualRect');
  CheckEqB(False, EqualRect(Rect(1, 2, 3, 4), Rect(1, 2, 3, 5)), 'EqualRect differs');
  R := Rect(0, 0, 10, 5);
  CheckEqB(True, PtInRect(R, Point(0, 0)), 'PtInRect top-left inside');
  CheckEqB(True, PtInRect(R, Point(9, 4)), 'PtInRect last inside');
  CheckEqB(False, PtInRect(R, Point(10, 4)), 'PtInRect right edge outside');
  CheckEqB(False, PtInRect(R, Point(9, 5)), 'PtInRect bottom edge outside');
  CheckEqB(False, PtInRect(R, Point(-1, 0)), 'PtInRect left of');
  CheckEqB(True, PointsEqual(Point(1, 2), Point(1, 2)), 'PointsEqual');
  CheckEqB(False, PointsEqual(Point(1, 2), Point(2, 1)), 'PointsEqual differs');
  P := CenterPoint(Rect(0, 0, 11, 7));
  Check((P.X = 5) and (P.Y = 3), 'CenterPoint');
  P := CenterPoint(Rect(10, 10, 0, 0));
  Check((P.X = 5) and (P.Y = 5), 'CenterPoint inverted');

  Section('modifiers');
  R := Rect(1, 2, 3, 4);
  CheckEqB(True, OffsetRect(R, 10, -2), 'OffsetRect result');
  CheckRect(R, 11, 0, 13, 2, 'OffsetRect');
  CheckEqB(True, InflateRect(R, 1, 2), 'InflateRect result');
  CheckRect(R, 10, -2, 14, 4, 'InflateRect');
  InflateRect(R, -3, -3);
  CheckRect(R, 13, 1, 11, 1, 'InflateRect negative');

  Section('intersection and union');
  CheckEqB(True, IntersectRect(R, Rect(0, 0, 10, 10), Rect(5, -5, 15, 5)), 'IntersectRect overlap');
  CheckRect(R, 5, 0, 10, 5, 'IntersectRect value');
  R := Rect(9, 9, 9, 9);
  CheckEqB(False, IntersectRect(R, Rect(0, 0, 10, 10), Rect(10, 0, 20, 10)), 'IntersectRect touching');
  CheckRect(R, 0, 0, 0, 0, 'IntersectRect empty is zero');
  R2 := Rect(0, 0, 10, 10);
  CheckEqB(True, IntersectRect(R2, R2, Rect(2, 3, 4, 5)), 'IntersectRect output aliases input');
  CheckRect(R2, 2, 3, 4, 5, 'IntersectRect aliased value');
  CheckEqB(True, UnionRect(R, Rect(0, 0, 2, 2), Rect(5, -1, 6, 1)), 'UnionRect');
  CheckRect(R, 0, -1, 6, 2, 'UnionRect value');
  CheckEqB(True, UnionRect(R, Rect(5, 5, 5, 9), Rect(1, 2, 3, 4)), 'UnionRect first empty');
  CheckRect(R, 1, 2, 3, 4, 'UnionRect ignores the empty input');
  CheckEqB(True, UnionRect(R, Rect(1, 2, 3, 4), Rect(8, 8, 0, 0)), 'UnionRect second empty');
  CheckRect(R, 1, 2, 3, 4, 'UnionRect ignores the empty second input');
  R := Rect(7, 7, 7, 7);
  CheckEqB(False, UnionRect(R, Rect(1, 1, 1, 5), Rect(4, 4, 2, 2)), 'UnionRect both empty');
  CheckRect(R, 0, 0, 0, 0, 'UnionRect both empty is zero');
  Done;
end.
