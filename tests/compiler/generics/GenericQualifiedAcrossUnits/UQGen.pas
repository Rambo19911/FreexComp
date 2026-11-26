unit UQGen;

interface

type
  TBox<T> = class
  public
    Value: T;
    constructor Create(const AValue: T);
    function Show: string;
  end;

  TOuter = class
  public
    type
      TInner = class
      public
        function Name: string;
      end;

      TCell<T> = class
      public
        Item: T;
        function Show: string;
      end;
  end;

  TList<T> = class
  public
    type
      TEnumerator = class
      private
        FList: TList<T>;
        FIndex: Integer;
      public
        constructor Create(AList: TList<T>);
        function MoveNext: Boolean;
        function GetCurrent: T;
        property Current: T read GetCurrent;
      end;
  private
    FItems: array of T;
  public
    procedure Add(const V: T);
    function GetEnumerator: TEnumerator;
  end;

implementation

constructor TBox<T>.Create(const AValue: T);
begin
  inherited Create;
  Value := AValue;
end;

function TBox<T>.Show: string;
begin
  Result := 'box ' + ClassName;
end;

function TOuter.TInner.Name: string;
begin
  Result := 'inner ' + ClassName;
end;

function TOuter.TCell<T>.Show: string;
begin
  Result := 'cell ' + ClassName;
end;

procedure TList<T>.Add(const V: T);
begin
  SetLength(FItems, Length(FItems) + 1);
  FItems[High(FItems)] := V;
end;

function TList<T>.GetEnumerator: TEnumerator;
begin
  Result := TEnumerator.Create(Self);
end;

constructor TList<T>.TEnumerator.Create(AList: TList<T>);
begin
  inherited Create;
  FList := AList;
  FIndex := -1;
end;

function TList<T>.TEnumerator.MoveNext: Boolean;
begin
  Inc(FIndex);
  Result := FIndex < Length(FList.FItems);
end;

function TList<T>.TEnumerator.GetCurrent: T;
begin
  Result := FList.FItems[FIndex];
end;

end.
