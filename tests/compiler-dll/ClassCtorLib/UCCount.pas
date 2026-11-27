unit UCCount;
// a generic whose class constructor counts its runs: each image that uses TCounter<Integer> runs it once (LANG-792)

interface

type
  TCounter<T> = class
  public
    class var Runs: Integer;
    class constructor Create;
  end;

implementation

class constructor TCounter<T>.Create;
begin
  Runs := Runs + 1;
end;

end.
