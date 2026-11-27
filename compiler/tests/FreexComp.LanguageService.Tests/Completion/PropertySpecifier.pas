{ context: property-specifier }
program PropertySpecifier;
type
  TItem = class
  private
    FSize: Integer;
  public
    property Size: Integer {|};
  end;
begin
end.
{#has read, write, default, stored, nodefault, index}
{#not FSize, Integer}
