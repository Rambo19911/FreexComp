// covers: LANG-630, LANG-633, LANG-624, CMP-367
program VirtualCtorThroughInstance;

type
  TBase = class
  public
    Tag: Integer;
    constructor Create(AValue: Integer); virtual;
  end;

  TBaseClass = class of TBase;

  TMid = class(TBase)
  public
    constructor Create(AValue: Integer); override;
  end;

  TLeaf = class(TMid)
  public
    constructor Create(AValue: Integer); override;
  end;

constructor TBase.Create(AValue: Integer);
begin
  inherited Create;
  Writeln('TBase.Create ', AValue);
  Tag := 1;
end;

constructor TMid.Create(AValue: Integer);
begin
  inherited Create(AValue);                { static: the ancestor's body only }
  Writeln('TMid.Create ', AValue);
  Tag := 2;
end;

constructor TLeaf.Create(AValue: Integer);
begin
  inherited Create(AValue);
  Writeln('TLeaf.Create ', AValue);
  Tag := 3;
end;

var
  C: TBaseClass;
  I, J: TBase;
  Before: NativeInt;
begin
  Before := _LiveAllocCount;

  { on an instance: no allocation, the virtual constructor dispatches on the instance's class }
  C := TLeaf;
  I := TBase(C.NewInstance);
  J := I;
  I.Create(7);
  Writeln(J = I, ' ', I.Tag, ' ', I.ClassName);
  I.AfterConstruction;
  I.Free;

  { a typecast receiver dispatches the same way }
  I := TBase(TMid.NewInstance);
  TBase(I).Create(8);
  Writeln(I.Tag, ' ', I.ClassName);
  I.Free;

  { a class-reference variable allocates and dispatches (unchanged) }
  C := TMid;
  I := C.Create(9);
  Writeln(I.Tag, ' ', I.ClassName);
  I.Free;

  { a class name binds statically (unchanged) }
  I := TBase.Create(10);
  Writeln(I.Tag, ' ', I.ClassName);
  I.Free;

  Writeln('leaked: ', _LiveAllocCount - Before);
end.
