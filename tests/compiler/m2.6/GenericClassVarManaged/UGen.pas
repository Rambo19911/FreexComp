unit UGen;

interface

type
  TRegistry<T> = class
  public
    class var Title: string;
    class var Items: array of T;
    class procedure Add(const V: T);
    class function Count: Integer;
  end;

implementation

class procedure TRegistry<T>.Add(const V: T);
begin
  SetLength(Items, Length(Items) + 1);
  Items[High(Items)] := V;
end;

class function TRegistry<T>.Count: Integer;
begin
  Result := Length(Items);
end;

end.
