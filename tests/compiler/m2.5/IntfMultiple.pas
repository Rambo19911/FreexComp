// covers: LANG-661, LANG-664, LANG-665, ABI-090, ABI-091
program IntfMultiple;

{ One class, two interfaces: each interface reference is the address of its own hidden field (listing order, 8 bytes
  apart, ABI section 7.1), and each dispatches to the right method. }

type
  IShape = interface
    ['{11111111-2222-3333-4444-555555555555}']
    function Area: Integer;
    procedure Grow(By: Integer);
  end;

  INamed = interface
    ['{11111111-2222-3333-4444-666666666666}']
    function Name: string;
  end;

  TSquare = class(TInterfacedObject, IShape, INamed)
  private
    FSide: Integer;
  public
    constructor Create(Side: Integer);
    destructor Destroy; override;
    function Area: Integer;
    procedure Grow(By: Integer);
    function Name: string;
  end;

constructor TSquare.Create(Side: Integer);
begin
  FSide := Side;
end;

destructor TSquare.Destroy;
begin
  Writeln('destroyed');
  inherited Destroy;
end;

function TSquare.Area: Integer;
begin
  Result := FSide * FSide;
end;

procedure TSquare.Grow(By: Integer);
begin
  FSide := FSide + By;
end;

function TSquare.Name: string;
begin
  Result := 'square';
end;

var
  Sq: TSquare;
  S: IShape;
  N: INamed;
begin
  Sq := TSquare.Create(7);
  S := Sq;
  N := Sq;
  Writeln(S.Area);
  S.Grow(1);
  Writeln(S.Area);
  Writeln(N.Name);
  Writeln(Pointer(S) <> Pointer(N));
  Writeln(NativeInt(Pointer(N)) - NativeInt(Pointer(S)));
  Writeln(NativeInt(Pointer(S)) > NativeInt(Pointer(Sq)));
  S := nil;
  Writeln('one left');
  N := nil;
  Writeln('end');
end.
