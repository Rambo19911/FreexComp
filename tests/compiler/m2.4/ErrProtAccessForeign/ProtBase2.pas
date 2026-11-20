unit ProtBase2;

interface

type
  TBase = class
  protected
    FValue: Integer;
    class var FCount: Integer;
    procedure Bump;
    class procedure Reset;
    property Value: Integer read FValue;
  end;

implementation

procedure TBase.Bump;
begin
  Inc(FValue);
end;

class procedure TBase.Reset;
begin
  FCount := 0;
end;

end.
