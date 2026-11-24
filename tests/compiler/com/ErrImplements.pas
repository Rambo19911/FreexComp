// covers: LANG-692
program ErrImplements;

type
  IFoo = interface
    ['{5E0C1A11-0B6F-4C1D-9A52-3D1F6E7A8C21}']
    procedure A;
    procedure B;
  end;

  IBar = interface
    ['{5E0C1A11-0B6F-4C1D-9A52-3D1F6E7A8C22}']
    procedure C;
  end;

  TImpl = class
    procedure A;
    procedure B(X: Integer);
  end;

  TFields = class(TInterfacedObject)
    FN: Integer;
    FBar: IBar;
    class var FShared: TImpl;
  end;

  // B of the delegate class has another signature: E2651 at the interface list entry
  TMissing = class(TFields, IFoo)
    FImpl: TImpl;
    property Impl: TImpl read FImpl implements IFoo;
  end;

  TChecks = class(TFields, IFoo)
    FFoo: IFoo;
    function GetItem(I: Integer): IFoo;
    property Foo: IFoo read FFoo implements IFoo, IBar;
    property NotAnInterface: IFoo read FFoo implements TImpl;
    property Again: IFoo read FFoo implements IFoo;
    property NoRead: IFoo write FFoo implements IFoo;
    property Items[I: Integer]: IFoo read GetItem implements IFoo;
    class property Shared: TImpl read FShared implements IFoo;
    property Number: Integer read FN implements IFoo;
    property Other: IBar read FBar implements IFoo;
  end;

procedure TImpl.A;
begin
end;

procedure TImpl.B(X: Integer);
begin
end;

function TChecks.GetItem(I: Integer): IFoo;
begin
  Result := FFoo;
end;

begin
end.
