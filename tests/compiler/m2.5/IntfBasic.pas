// covers: LANG-660, LANG-661, LANG-664
program IntfBasic;

{ A class implements an interface; calls go through the interface (ABI-093). }

type
  IGreeter = interface
    ['{6F1D2A10-3B4C-4D5E-8F90-A1B2C3D4E5F6}']
    procedure Greet(const Name: string);
    function Count: Integer;
  end;

  TGreeter = class(TInterfacedObject, IGreeter)
  private
    FCount: Integer;
  public
    procedure Greet(const Name: string);
    function Count: Integer;
  end;

procedure TGreeter.Greet(const Name: string);
begin
  Inc(FCount);
  Writeln('Hello, ', Name);
end;

function TGreeter.Count: Integer;
begin
  Result := FCount;
end;

var
  G: IGreeter;
begin
  G := TGreeter.Create;
  G.Greet('World');
  G.Greet('Pascal');
  Writeln(G.Count);
  G := nil;
  Writeln('done');
end.
