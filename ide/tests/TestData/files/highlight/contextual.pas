type
  T = class
    procedure P; virtual; abstract;
    property V: Integer read FV write FV default 0;
  end;
var
  read: Integer;
