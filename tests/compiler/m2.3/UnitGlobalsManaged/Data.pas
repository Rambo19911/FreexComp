unit Data;

interface

type
  TPair = record
    Name: string;
    Count: Integer;
  end;

var
  Greeting: string;
  Numbers: array of Integer;
  Pair: TPair;

implementation

var
  Hidden: string;

initialization
  Greeting := 'hello';
  Greeting := Greeting + ' world';
  SetLength(Numbers, 3);
  Pair.Name := Greeting + '?';
  Hidden := Pair.Name + Greeting;
finalization
  // user statements run first; the managed globals are released after them (LANG-523)
  Writeln('final Data ', Hidden);
end.