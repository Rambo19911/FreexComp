unit Sample;

interface

uses
  SysUtils, Classes;

type
  TShape = (shCircle, shSquare, shTriangle);

  TPoint2 = record
    X, Y: Integer;
  end;

  TCanvasItem = class(TObject)
  strict private
    FSecret: Integer;
  private
    FName: string;
    procedure SetName(const AValue: string);
  strict protected
    FGuarded: Boolean;
  protected
    FShape: TShape;
    function GetArea: Double; virtual;
  public
    constructor Create(const AName: string);
    procedure Draw;
    property Name: string read FName write SetName;
  published
    property Shape: TShape read FShape write FShape;
  end;

const
  MaxItems = 100;
  Greeting = 'Hello, structure window';

var
  ItemCount: Integer;
  Origin: TPoint2;

function Scale(A: Integer): Integer; overload;
function Scale(A: Double): Double; overload;

implementation

uses
  Math;

constructor TCanvasItem.Create(const AName: string);
begin
  FName := AName;
end;

procedure TCanvasItem.SetName(const AValue: string);
begin
  FName := AValue;
end;

function TCanvasItem.GetArea: Double;
begin
  Result := 0;
end;

procedure TCanvasItem.Draw;

  procedure DrawOutline;
  begin
    Inc(ItemCount);
  end;

begin
  DrawOutline;
end;

function Scale(A: Integer): Integer;
begin
  Result := A * 2;
end;

function Scale(A: Double): Double;
begin
  Result := A * 2.0 +;
end;

end.
