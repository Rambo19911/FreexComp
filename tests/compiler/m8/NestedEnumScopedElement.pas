// covers: LANG-770
program NestedEnumScopedElement;

type
  TOuter = record
  public
    type
      TKind = (kNone, kSmall, kLarge);
      TInner = record
      public
        type
          TMode = (mOff, mOn);
      end;
  public
    Kind: TKind;
  end;

  TShape = class
  public
    type
      TColor = (cRed, cGreen, cBlue);
    function Pick(C: TColor): Integer;
  end;

function TShape.Pick(C: TColor): Integer;
begin
  case C of
    TShape.TColor.cRed: Result := 1;
    TColor.cGreen: Result := 2;
  else
    Result := 3;
  end;
end;

const
  Chosen = TOuter.TKind.kLarge;

var
  R: TOuter;
  K: TOuter.TKind;
  M: TOuter.TInner.TMode;
  S: TShape;
begin
  R.Kind := TOuter.TKind.kSmall;
  K := TOuter.TKind.kLarge;
  M := TOuter.TInner.TMode.mOn;
  WriteLn(Ord(R.Kind));
  WriteLn(Ord(K));
  WriteLn(Ord(M));
  WriteLn(Ord(Chosen));
  WriteLn(K = TOuter.TKind.kLarge);
  WriteLn(Ord(High(TOuter.TKind)));
  S := TShape.Create;
  WriteLn(S.Pick(TShape.TColor.cRed));
  WriteLn(S.Pick(TShape.TColor.cGreen));
  WriteLn(S.Pick(TShape.TColor.cBlue));
  S.Free;
end.
