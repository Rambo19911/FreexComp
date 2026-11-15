// covers: UIF-505, UIF-507, UIF-520, UIF-525
program Gfx_PenBrush;
{ U-005, graphics.md sections 2.2 and 2.3: TPen and TBrush defaults, handles built per UIF-520 / UIF-525
  (read back with GetObjectW), the UIF-505 change order, Mode is not part of the pen handle, bsClear uses the
  stock NULL_BRUSH, setting Color while bsClear switches to bsSolid, Assign and Handle := external. }
uses
  Windows, SysUtils, Classes, Graphics, TestKit;

type
  TLogPenRec = record
    lopnStyle: UINT;
    lopnWidth: TPoint;
    lopnColor: COLORREF;
  end;

  TRecorder = class
    Obj: TGraphicsObject;
    Watched: HGDIOBJ;
    Log: string;
    procedure Changed(Sender: TObject);
  end;

function YesNo(B: Boolean): string;
begin
  if B then
    Result := 'y'
  else
    Result := 'n';
end;

{ GetObjectW with no buffer returns the object's record size. Deletion itself is not asserted for pens and brushes:
  GDI keeps deleted pens and brushes in a per-process cache where the handle stays valid (Gfx_Font asserts the
  shared UIF-505 path with fonts, which are not cached). }
function Alive(H: HGDIOBJ): Boolean;
begin
  Result := GetObjectW(H, 0, nil) <> 0;
end;

procedure TRecorder.Changed(Sender: TObject);
begin
  Log := Log + 'change(allocated=' + YesNo(Obj.HandleAllocated) + ' old=' + YesNo(Alive(Watched)) + ');';
end;

function PenOf(H: HGDIOBJ): TLogPenRec;
begin
  FillChar(Result, SizeOf(Result), 0);
  GetObjectW(H, SizeOf(Result), @Result);
end;

function BrushOf(H: HGDIOBJ): TLogBrush;
begin
  FillChar(Result, SizeOf(Result), 0);
  GetObjectW(H, SizeOf(Result), @Result);
end;

procedure CheckPen;
var
  P, Q: TPen;
  R: TRecorder;
  L: TLogPenRec;
  S: TPenStyle;
  H, Ext: HPEN;
begin
  Section('TPen defaults');
  P := TPen.Create;
  R := TRecorder.Create;
  try
    CheckEqI(clBlack, P.Color, 'Color');
    Check(P.Mode = pmCopy, 'Mode');
    Check(P.Style = psSolid, 'Style');
    CheckEqI(1, P.Width, 'Width');
    CheckEqB(False, P.HandleAllocated, 'no handle before first read');

    Section('TPen handle (UIF-520)');
    for S := psSolid to psInsideFrame do
    begin
      P.Style := S;
      L := PenOf(P.Handle);
      CheckEqI(Ord(S), L.lopnStyle, 'PS_ value of style ' + IntStr(Ord(S)));
    end;
    P.Style := psDash;
    P.Width := 3;
    P.Color := clRed;
    L := PenOf(P.Handle);
    CheckEqI(PS_DASH, L.lopnStyle, 'lopnStyle');
    CheckEqI(3, L.lopnWidth.X, 'lopnWidth');
    CheckEqI($0000FF, L.lopnColor, 'lopnColor');
    P.Color := clBtnShadow;
    CheckEqI(GetSysColor(COLOR_BTNSHADOW), PenOf(P.Handle).lopnColor, 'system colour resolved');

    Section('TPen change order (UIF-505)');
    R.Obj := P;
    P.OnChange := R.Changed;
    H := P.Handle;
    R.Watched := H;
    P.Width := 3;
    P.Style := psDash;
    P.Color := clBtnShadow;
    CheckEqS('', R.Log, 'unchanged values fire nothing');
    P.Mode := pmXor;
    CheckEqS('change(allocated=y old=y);', R.Log, 'Mode keeps the handle');
    R.Log := '';
    P.Width := 2;
    CheckEqS('change(allocated=n old=y);', R.Log, 'Width replaces the handle');

    Section('TPen Assign and external handle');
    Q := TPen.Create;
    try
      Q.Assign(P);
      CheckEqI(P.Color, Q.Color, 'Color copied');
      Check(Q.Mode = pmXor, 'Mode copied');
      Check(Q.Style = psDash, 'Style copied');
      CheckEqI(2, Q.Width, 'Width copied');
      Ext := CreatePen(PS_DOT, 1, $00112233);
      Q.Handle := Ext;
      Check(Q.Style = psDot, 'Style read back');
      CheckEqI(1, Q.Width, 'Width read back');
      CheckEqI($112233, Q.Color, 'Color read back');
      CheckEqB(True, Q.Handle = Ext, 'external pen kept');
    finally
      Q.Free;
    end;
  finally
    P.Free;
    R.Free;
  end;
end;

procedure CheckBrush;
var
  B, C: TBrush;
  R: TRecorder;
  L: TLogBrush;
  S: TBrushStyle;
  H, Ext: HBRUSH;
begin
  Section('TBrush defaults');
  B := TBrush.Create;
  R := TRecorder.Create;
  try
    CheckEqI(clWhite, B.Color, 'Color');
    Check(B.Style = bsSolid, 'Style');
    CheckEqB(False, B.HandleAllocated, 'no handle before first read');

    Section('TBrush handle (UIF-525)');
    B.Color := clLime;
    L := BrushOf(B.Handle);
    CheckEqI(BS_SOLID, L.lbStyle, 'bsSolid is BS_SOLID');
    CheckEqI($00FF00, L.lbColor, 'solid colour');
    B.Style := bsClear;
    CheckEqB(True, B.Handle = GetStockObject(NULL_BRUSH), 'bsClear is the stock NULL_BRUSH');
    for S := bsHorizontal to bsDiagCross do
    begin
      B.Style := S;
      L := BrushOf(B.Handle);
      CheckEqI(BS_HATCHED, L.lbStyle, 'hatched style ' + IntStr(Ord(S)));
      CheckEqI(Ord(S) - Ord(bsHorizontal), L.lbHatch, 'HS_ value of style ' + IntStr(Ord(S)));
      CheckEqI($00FF00, L.lbColor, 'hatch colour ' + IntStr(Ord(S)));
    end;

    Section('TBrush change order and bsClear rule');
    R.Obj := B;
    B.OnChange := R.Changed;
    B.Style := bsClear;
    R.Log := '';
    H := B.Handle;
    R.Watched := H;
    B.Color := clYellow;
    CheckEqS('change(allocated=n old=y);', R.Log, 'one event');
    Check(B.Style = bsSolid, 'Color while bsClear sets bsSolid');
    CheckEqB(True, Alive(GetStockObject(NULL_BRUSH)), 'stock brush not deleted');
    CheckEqI($00FFFF, BrushOf(B.Handle).lbColor, 'new solid brush colour');
    R.Log := '';
    H := B.Handle;
    R.Watched := H;
    B.Color := clYellow;
    B.Style := bsSolid;
    CheckEqS('', R.Log, 'unchanged values fire nothing');
    B.Style := bsCross;
    CheckEqS('change(allocated=n old=y);', R.Log, 'Style replaces the handle');

    Section('TBrush Assign and external handle');
    C := TBrush.Create;
    try
      C.Assign(B);
      CheckEqI(clYellow, C.Color, 'Color copied');
      Check(C.Style = bsCross, 'Style copied');
      Ext := CreateSolidBrush($00445566);
      C.Handle := Ext;
      Check(C.Style = bsSolid, 'Style read back');
      CheckEqI($445566, C.Color, 'Color read back');
      CheckEqB(True, C.Handle = Ext, 'external brush kept');
    finally
      C.Free;
    end;
  finally
    B.Free;
    R.Free;
  end;
end;

begin
  CheckPen;
  CheckBrush;
  Done;
end.
