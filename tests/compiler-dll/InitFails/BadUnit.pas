unit BadUnit;

interface

implementation

uses SysUtils, FirstUnit;

initialization
  raise Exception.Create('initialization failed on purpose');
finalization
  Mark('bad-final.txt');
end.
