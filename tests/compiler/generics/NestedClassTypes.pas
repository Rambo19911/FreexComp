// covers: LANG-771, LANG-772, LANG-773, LANG-774, LANG-775, LANG-776, LANG-777, CMP-1390, CMP-1391
program NestedClassTypes;

type
  TOuter = class
  strict private
    class var FCount: Integer;
  private
    FName: string;
  public
    type
      IInner = interface
        function Describe: string;
      end;

      TLater = class;

      TInner = class
      private
        FOwner: TOuter;
      public
        constructor Create(AOwner: TOuter);
        function M: string;
        function Later: TLater;
      end;

      TImpl = class(TInterfacedObject, IInner)
      public
        function Describe: string;
      end;

      TLater = class(TInner)
      public
        function Tag: Integer;
      end;

      TA = class
      public
        type
          TB = class
          public
            function M: string;
          end;
      end;
  public
    constructor Create(const AName: string);
    function MakeInner: TInner;
  end;

  TRec = record
    Value: Integer;
    type
      TBox = class
      public
        V: Integer;
        function Doubled: Integer;
      end;
  end;

constructor TOuter.Create(const AName: string);
begin
  inherited Create;
  FName := AName;
  Inc(FCount);
end;

function TOuter.MakeInner: TInner;
begin
  Result := TInner.Create(Self);
end;

constructor TOuter.TInner.Create(AOwner: TOuter);
begin
  inherited Create;
  FOwner := AOwner;
end;

function TOuter.TInner.M: string;
begin
  // a strict private class var of the enclosing type by simple name, an instance field through the owner reference
  Result := FOwner.FName + ' count=' + Chr(Ord('0') + FCount);
end;

function TOuter.TInner.Later: TLater;
begin
  Result := TLater.Create(FOwner);
end;

function TOuter.TImpl.Describe: string;
begin
  Result := 'impl ' + ClassName;
end;

function TOuter.TLater.Tag: Integer;
begin
  Result := 42;
end;

function TOuter.TA.TB.M: string;
begin
  Result := 'deep ' + ClassName;
end;

function TRec.TBox.Doubled: Integer;
begin
  Result := V * 2;
end;

procedure Run;
var
  O: TOuter;
  I: TOuter.TInner;
  L: TOuter.TLater;
  B: TOuter.TA.TB;
  X: TOuter.IInner;
  Obj: TObject;
  R: TRec.TBox;
begin
  O := TOuter.Create('first');
  I := O.MakeInner;
  WriteLn(I.M);
  I.Free;
  I := TOuter.TInner.Create(O);
  Obj := I;
  WriteLn(Obj is TOuter.TInner);
  WriteLn(Obj is TOuter.TLater);
  WriteLn(I.ClassName);
  WriteLn(I.UnitName);
  L := I.Later;
  WriteLn(L.ClassName, ' ', L.Tag, ' ', L.M);
  Obj := L;
  WriteLn(Obj is TOuter.TInner);
  L.Free;
  I.Free;
  B := TOuter.TA.TB.Create;
  WriteLn(B.M);
  B.Free;
  X := TOuter.TImpl.Create;
  WriteLn(X.Describe);
  X := nil;
  R := TRec.TBox.Create;
  R.V := 21;
  WriteLn(R.Doubled, ' ', R.ClassName);
  R.Free;
  O.Free;
end;

var
  Live: NativeInt;
begin
  Live := _LiveAllocCount;
  Run;
  WriteLn('leaks ', _LiveAllocCount - Live);
end.
