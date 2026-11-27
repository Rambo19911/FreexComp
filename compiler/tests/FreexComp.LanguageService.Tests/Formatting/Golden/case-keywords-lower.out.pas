program CaseKeywords;

type
  TShape = class
  private
    FSize: Integer;
  public
    constructor Create; virtual;
    function Area: Integer; virtual; abstract;
    property Size: Integer read FSize write FSize;
  end;

constructor TShape.Create;
begin
  FSize := 1;
end;

var
  I: Integer;
  S: string;
begin
  S := 'Begin End';    { Begin And End in a comment }
  for I := 1 to 3 do
    if (I mod 2 = 0) and not (I = 4) then
      Writeln(S)
    else
      Writeln(I div 1, ' ', nil = nil);
end.
