// covers: LANG-650, LANG-651, LANG-665, CMP-373
program IntfAsIs;

{ 'is' and 'as' with interfaces through the GUID: object is/as interface (the class's own and an ancestor's interface,
  an interface listed only through a descendant interface, an unsupported one, nil), interface as interface
  (QueryInterface), and a Supports-style test. The object is freed when the last counted reference goes. }

type
  IShape = interface
    ['{11111111-2222-3333-4444-555555555555}']
    function Area: Integer;
  end;

  ISquare = interface(IShape)
    ['{22222222-3333-4444-5555-666666666666}']
    function Side: Integer;
  end;

  INamed = interface
    ['{aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee}']
    function Name: string;
  end;

  ISilent = interface
    ['{99999999-8888-7777-6666-555555555555}']
    procedure Hush;
  end;

  TSquare = class(TInterfacedObject, ISquare)
  private
    FSide: Integer;
  public
    constructor Create(ASide: Integer);
    destructor Destroy; override;
    function Area: Integer;
    function Side: Integer;
  end;

  TNamedSquare = class(TSquare, INamed)
    function Name: string;
  end;

  TPlain = class(TInterfacedObject)
  end;

constructor TSquare.Create(ASide: Integer);
begin
  FSide := ASide;
end;

destructor TSquare.Destroy;
begin
  Writeln('destroyed ', FSide);
  inherited Destroy;
end;

function TSquare.Area: Integer;
begin
  Result := FSide * FSide;
end;

function TSquare.Side: Integer;
begin
  Result := FSide;
end;

function TNamedSquare.Name: string;
begin
  Result := 'square';
end;

function Supports(O: TObject): string;
begin
  if O is INamed then
    Result := 'named'
  else if O is IShape then
    Result := 'shape'
  else
    Result := 'none';
end;

var
  O: TObject;
  P: TPlain;
  Shape: IShape;
  Square: ISquare;
  Named: INamed;
begin
  O := TNamedSquare.Create(3);
  Writeln(O is IShape, ' ', O is ISquare, ' ', O is INamed, ' ', O is ISilent);
  Shape := O as IShape;
  Writeln(Shape.Area);
  Named := Shape as INamed;
  Writeln(Named.Name);
  Square := Named as ISquare;
  Writeln(Square.Side, ' ', Square.Area);
  Shape := nil;
  Square := nil;
  Writeln(Named.Name);
  Writeln(Supports(O), ' ', Supports(nil));
  P := TPlain.Create;
  Writeln(Supports(P), ' ', P is IShape);
  P.Free;
  O := nil;
  Writeln(O is IShape);
  Shape := O as IShape;
  Writeln(Shape = nil);
  Shape := Named as IShape;
  Named := nil;
  Writeln(Shape.Area);
  Shape := nil;
  Writeln('done');
end.
