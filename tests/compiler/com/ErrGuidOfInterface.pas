// covers: LANG-680, LANG-681, LANG-682
program ErrGuidOfInterface;

type
  IPlain = interface
  end;

  IFoo = interface
    ['{5E0C1A11-0B6F-4C1D-9A52-3D1F6E7A8B02}']
  end;

  IBar = interface
    ['{5E0C1A11-0B6F-4C1D-9A52-3D1F6E7A8B03}']
  end;

  PInteger = ^Integer;

const
  NoGuid: TGUID = IPlain;
  Short: TGUID = '{5E0C1A11-0B6F-4C1D-9A52}';
  NoBraces: TGUID = '5E0C1A11-0B6F-4C1D-9A52-3D1F6E7A8B02';

procedure Take(const G: TGUID);
begin
end;

var
  G: TGUID;
  O: TObject;
  F: IFoo;
  B: IBar;
  N: NativeInt;
  P: PInteger;
begin
  Take(IPlain);
  G := IPlain;
  O := nil;
  F := IFoo(O);
  B := nil;
  F := IFoo(B);
  N := 0;
  F := IFoo(N);
  P := nil;
  F := IFoo(P);
  F := IFoo(TObject.Create);
end.
