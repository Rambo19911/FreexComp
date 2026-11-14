unit Foo;

interface

type
  TInner = record
    Value: Integer;
  end;

  TOuter = record
    Tag: Integer;
    Baz: TInner;
  end;

var
  Bar: TOuter;

implementation

initialization
  Bar.Tag := 7;
  Bar.Baz.Value := 100;
end.
