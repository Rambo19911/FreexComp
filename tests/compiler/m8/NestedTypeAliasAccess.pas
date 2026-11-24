// covers: LANG-770
program NestedTypeAliasAccess;

type
  TLevel = (lvLow, lvMid, lvHigh);
  TPoint = record
    X, Y: Integer;
  end;

  TR = record
  public
    type
      TA = TLevel;
      TP = TPoint;
      TCount = Integer;
  public
    Level: TA;
    function Bump: TA;
  end;

  TC = class
  public
    type
      TA = TLevel;
      TText = string;
  end;

function TR.Bump: TA;
var
  C: TCount;
begin
  C := Ord(Level) + 1;
  Result := TA(C);
end;

function Next(L: TR.TA): TR.TA;
begin
  if L = High(TR.TA) then
    Result := Low(TR.TA)
  else
    Result := Succ(L);
end;

var
  A: TR.TA;
  B: TC.TA;
  P: TR.TP;
  N: TR.TCount;
  T: TC.TText;
  R: TR;
begin
  A := lvMid;
  B := A;
  A := TR.TA.lvHigh;
  P.X := 3;
  P.Y := 4;
  N := 5;
  T := 'text';
  R.Level := Next(A);
  WriteLn(Ord(A));
  WriteLn(Ord(B));
  WriteLn(P.X + P.Y);
  WriteLn(N);
  WriteLn(T);
  WriteLn(Ord(R.Level));
  WriteLn(Ord(TR.TA(2)));
  WriteLn(SizeOf(TC.TA));
  R.Level := lvLow;
  WriteLn(Ord(R.Bump));
end.
