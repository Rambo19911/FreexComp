program NestedType;
type
  TOuter = class
  public
    type
      {#}TInner = class
        Value: Integer;
      end;
  end;
var
  I: TOuter.{|}TInner;
begin
  I := nil;
  Writeln(I = nil);
end.
