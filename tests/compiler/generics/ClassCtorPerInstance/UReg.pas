unit UReg;

{ declares the generic only; it uses no instance, so it runs none of them }

interface

type
  TRegistry<T> = class
  public
    class var Count: Integer;
    class constructor Create;
    class destructor Destroy;
    type
      TEntry = class
        class var Hits: Integer;
        class constructor Init;
      end;
  end;

var
  Runs: Integer;

implementation

class constructor TRegistry<T>.Create;
begin
  Inc(Runs);
  Count := Count + 1;
  Writeln('cctor ', SizeOf(T));
end;

class destructor TRegistry<T>.Destroy;
begin
  Writeln('cdtor ', SizeOf(T));
end;

class constructor TRegistry<T>.TEntry.Init;
begin
  Inc(Hits);
  Writeln('entry cctor ', SizeOf(T));
end;

end.
