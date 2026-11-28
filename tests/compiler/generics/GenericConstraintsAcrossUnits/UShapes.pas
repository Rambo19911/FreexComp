unit UShapes;

interface

type
  TBase = class
  public
    function Name: string; virtual;
  end;

  IGreeter = interface
    function Greet: string;
  end;

  // class, constructor: Make creates the type argument through the constructor the call X.Create resolves to
  TFactory<T: class, constructor> = class
  public
    class function Make: T;
  end;

  // a class type: the members of TBase are called on the type argument
  TNamer<T: TBase> = class
  public
    class function NameOf(const Item: T): string;
  end;

  // generic methods: an interface type, a class type with constructor, a nested type of the class, the class itself
  TTools = class
  public
    type
      TTag = class
      public
        function Text: string; virtual;
      end;
    class function Greet<T: IGreeter>(const Item: T): string; static;
    class function Fresh<T: TBase, constructor>: string; static;
    class function Tagged<T: TTag>(const Item: T; const Other: TTag): string; static;
    function Same<T: TTools>(const Other: T): Boolean;
  end;

implementation

function TBase.Name: string;
begin
  Result := 'base';
end;

class function TFactory<T>.Make: T;
begin
  Result := T.Create;
end;

class function TNamer<T>.NameOf(const Item: T): string;
begin
  Result := Item.Name;
end;

function TTools.TTag.Text: string;
begin
  Result := 'tag';
end;

class function TTools.Greet<T>(const Item: T): string;
begin
  Result := Item.Greet;
end;

class function TTools.Fresh<T>: string;
var
  Made: T;
begin
  Made := T.Create;
  Result := Made.Name;
  Made.Free;
end;

class function TTools.Tagged<T>(const Item: T; const Other: TTag): string;
begin
  Result := Item.Text + '/' + Other.Text;
end;

function TTools.Same<T>(const Other: T): Boolean;
begin
  Result := Other = Self;
end;

end.
