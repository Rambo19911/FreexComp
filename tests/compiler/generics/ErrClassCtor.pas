// covers: LANG-786, LANG-788
program ErrClassCtor;

type
  TParams = class
    class constructor Create(X: Integer);
    class destructor Destroy<T>;
  end;

  TTwice = class
    class constructor Create;
    class constructor Init;
  end;

  TRecTwice = record
    X: Integer;
    class destructor Done;
    class destructor Finish;
  end;

  TDirectives = class
    class constructor Create; virtual;
    class destructor Destroy; static;
  end;

  TBody = class
    FField: Integer;
    class constructor Create;
    class destructor Destroy;
  end;

  TMissing = class
    class constructor Create;
  end;

  TFoo = class
    class constructor Create;
  end;

class constructor TParams.Create(X: Integer);
begin
end;

class destructor TParams.Destroy;
begin
end;

class constructor TTwice.Create;
begin
end;

class destructor TRecTwice.Done;
begin
end;

class constructor TDirectives.Create;
begin
end;

class destructor TDirectives.Destroy;
begin
end;

class constructor TBody.Create;
begin
  FField := 1;
end;

class destructor TBody.Destroy;
begin
  inherited;
end;

class constructor TFoo.Init;
begin
end;

class constructor TFoo.Create;
begin
end;

begin
end.
