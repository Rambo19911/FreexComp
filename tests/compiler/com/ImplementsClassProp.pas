// covers: LANG-690, LANG-692
program ImplementsClassProp;

uses SysUtils;

{ Delegation to a class-typed property: the delegated methods are found in the property's class by ordinary member lookup,
  so a virtual method overridden in a descendant of the delegate's class is the one that runs. One method of the interface
  is taken back by a method resolution clause. A descendant that lists the interface again reuses the inherited delegating
  methods (the method taken back by the clause needs a clause again), unless it declares a method of its own. }

type
  IShape = interface
    ['{5E0C1A11-0B6F-4C1D-9A52-3D1F6E7A8C11}']
    function Name: string;
    function Area(Scale: Integer): Integer;
    procedure Describe(const Prefix: string; out Text: string);
    function Sum(const Values: array of Integer): Integer;
  end;

  TShapeImpl = class
    FSide: Integer;
    constructor Create(Side: Integer);
    function Name: string; virtual;
    function Area(Scale: Integer): Integer; virtual;
    procedure Describe(const Prefix: string; out Text: string);
    function Sum(const Values: array of Integer): Integer;
  end;

  TSquareImpl = class(TShapeImpl)
    function Name: string; override;
    function Area(Scale: Integer): Integer; override;
  end;

  TOuter = class(TInterfacedObject, IShape)
  private
    FImpl: TShapeImpl;
    function OuterName: string;
    function IShape.Name = OuterName;
  public
    constructor Create(Impl: TShapeImpl);
    destructor Destroy; override;
    property Impl: TShapeImpl read FImpl implements IShape;
  end;

  TSame = class(TOuter, IShape)
    function IShape.Name = OuterName;
  end;

  TOwn = class(TOuter, IShape)
    function Area(Scale: Integer): Integer;
    function Name: string;
  end;

constructor TShapeImpl.Create(Side: Integer);
begin
  inherited Create;
  FSide := Side;
end;

function TShapeImpl.Name: string;
begin
  Result := 'shape';
end;

function TShapeImpl.Area(Scale: Integer): Integer;
begin
  Result := 0;
end;

procedure TShapeImpl.Describe(const Prefix: string; out Text: string);
begin
  Text := Prefix + Name + ' of side ' + IntToStr(FSide);
end;

function TShapeImpl.Sum(const Values: array of Integer): Integer;
var
  I: Integer;
begin
  Result := 0;
  for I := Low(Values) to High(Values) do
    Result := Result + Values[I];
end;

function TSquareImpl.Name: string;
begin
  Result := 'square';
end;

function TSquareImpl.Area(Scale: Integer): Integer;
begin
  Result := FSide * FSide * Scale;
end;

constructor TOuter.Create(Impl: TShapeImpl);
begin
  inherited Create;
  FImpl := Impl;
end;

destructor TOuter.Destroy;
begin
  FImpl.Free;
  inherited Destroy;
end;

function TOuter.OuterName: string;
begin
  Result := 'outer(' + FImpl.Name + ')';
end;

function TOwn.Area(Scale: Integer): Integer;
begin
  Result := -Scale;
end;

function TOwn.Name: string;
begin
  Result := 'own';
end;

procedure Show(const Title: string; const S: IShape);
var
  Text: string;
begin
  WriteLn(Title);
  WriteLn('  name ', S.Name);
  WriteLn('  area ', S.Area(2));
  S.Describe('a ', Text);
  WriteLn('  ', Text);
  WriteLn('  sum ', S.Sum([1, 2, 3, 4]));
end;

begin
  Show('base delegate', TOuter.Create(TShapeImpl.Create(3)));
  Show('square delegate', TOuter.Create(TSquareImpl.Create(3)));
  Show('descendant, inherited delegation', TSame.Create(TSquareImpl.Create(4)));
  Show('descendant with its own methods', TOwn.Create(TSquareImpl.Create(5)));
  WriteLn('done');
end.
