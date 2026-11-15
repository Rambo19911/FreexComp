unit UGenList;

interface

type
  TCounted<T> = class
  private
    FItems: array of T;
  public
    class var Instances: Integer;
    constructor Create;
    procedure Add(const V: T);
    function Count: Integer;
    function Get(I: Integer): T;
    function Total: Integer;
  end;

function Calls: Integer;

implementation

var
  CallCount: Integer;

procedure Track;
begin
  Inc(CallCount);
end;

function Calls: Integer;
begin
  Result := CallCount;
end;

constructor TCounted<T>.Create;
begin
  inherited Create;
  Inc(Instances);
end;

procedure TCounted<T>.Add(const V: T);
begin
  Track;
  SetLength(FItems, Length(FItems) + 1);
  FItems[High(FItems)] := V;
end;

function TCounted<T>.Count: Integer;
begin
  Result := Length(FItems);
end;

function TCounted<T>.Get(I: Integer): T;
begin
  Result := FItems[I];
end;

function TCounted<T>.Total: Integer;
begin
  Result := CallCount * 100 + Length(FItems);
end;

end.
