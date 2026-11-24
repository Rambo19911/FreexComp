unit FailSecond;

interface

implementation

uses SysUtils, FailFirst;

initialization
  raise Exception.Create('initialization failed on purpose');
finalization
  Mark('second-final.txt');
end.
