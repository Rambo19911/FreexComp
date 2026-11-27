program TypeReference;
type
  {#}TShape = class
  end;
var
  S: {|}TShape;
begin
  S := nil;
  Writeln(S = nil);
end.
