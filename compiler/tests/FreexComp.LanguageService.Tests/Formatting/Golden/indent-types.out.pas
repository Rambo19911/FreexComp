unit IndentTypes;
interface
type
  TShape = class(TObject)
  private
    FName: string;
  strict protected
    procedure Changed; virtual;
  public
    constructor Create(const AName: string);
    property Name: string read FName write FName;
  end;
  TPoint = record
    X, Y: Integer;
  end;
  IShape = interface
    ['{6B1E6A5A-0C4B-4E43-9B8C-1A2B3C4D5E6F}']
    function Area: Double;
  end;
  TVariant = record
    Kind: Integer;
    case Tag: Integer of
      0: (I: Integer);
      1: (D: Double;
        B: Byte);
  end;
  TForward = class;
  TEmpty = class
  end;

implementation
constructor TShape.Create(const AName: string);
begin
  FName := AName;
end;

procedure TShape.Changed;
begin
end;
end.
