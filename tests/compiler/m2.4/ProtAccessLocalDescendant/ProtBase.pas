unit ProtBase;

interface

type
  TBase = class
  protected
    FValue: Integer;
    class var FCount: Integer;
    function Twice: Integer;
    class function Kind: string;
    class function GetCount: Integer; static;
    property Value: Integer read FValue write FValue;
    class property Count: Integer read GetCount;
  public
    constructor Create(AValue: Integer);
  end;

implementation

constructor TBase.Create(AValue: Integer);
begin
  FValue := AValue;
  Inc(FCount);
end;

function TBase.Twice: Integer;
begin
  Result := FValue * 2;
end;

class function TBase.Kind: string;
begin
  Result := 'base';
end;

class function TBase.GetCount: Integer;
begin
  Result := FCount;
end;

end.
