unit UShapeKinds;

interface

uses
  UShapes;

type
  TCircle = class(TBase)
  public
    function Name: string; override;
  end;

  // a constructor whose parameter has a default value: T.Create calls Create(4)
  TSquare = class(TBase)
  private
    FSides: Integer;
  public
    constructor Create(ASides: Integer = 4);
    function Name: string; override;
  end;

  // Create(...) with overload: the parameterless constructor of the ancestor stays visible, T.Create calls that one
  TLabelled = class(TBase)
  private
    FText: string;
  public
    constructor Create(const AText: string); overload;
    function Name: string; override;
  end;

  THello = class(TInterfacedObject, IGreeter)
  public
    function Greet: string;
  end;

  TLoudTag = class(TTools.TTag)
  public
    function Text: string; override;
  end;

  TPowerTools = class(TTools)
  end;

implementation

function TCircle.Name: string;
begin
  Result := 'circle';
end;

constructor TSquare.Create(ASides: Integer);
begin
  FSides := ASides;
end;

function TSquare.Name: string;
var
  S: string;
begin
  Str(FSides, S);
  Result := 'square ' + S;
end;

constructor TLabelled.Create(const AText: string);
begin
  FText := AText;
end;

function TLabelled.Name: string;
begin
  Result := 'labelled [' + FText + ']';
end;

function THello.Greet: string;
begin
  Result := 'hello';
end;

function TLoudTag.Text: string;
begin
  Result := 'LOUD';
end;

end.
