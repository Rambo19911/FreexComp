unit UReg;

{ a class with a class constructor and class destructor, then a record helper for Integer with a class var, a class constructor
  and a class destructor: the helper is an item of its unit in declaration order (LANG-995), its class constructor runs with the
  unit's initialization whether or not the helper is ever used, its class destructor after the finalization in reverse order }

interface

type
  TThing = class
    class constructor Create;
    class destructor Destroy;
  end;

  TIntHelper = record helper for Integer
    class var Calls: Integer;
    class constructor Create;
    class destructor Destroy;
    function Twice: Integer;
  end;

implementation

class constructor TThing.Create;
begin
  Writeln('ctor TThing');
end;

class destructor TThing.Destroy;
begin
  Writeln('dtor TThing');
end;

class constructor TIntHelper.Create;
begin
  Calls := 10;
  Writeln('ctor TIntHelper');
end;

class destructor TIntHelper.Destroy;
begin
  Writeln('dtor TIntHelper');
end;

function TIntHelper.Twice: Integer;
begin
  Calls := Calls + 1;
  Result := Self * 2;
end;

initialization
  Writeln('init UReg');

finalization
  Writeln('final UReg');

end.
