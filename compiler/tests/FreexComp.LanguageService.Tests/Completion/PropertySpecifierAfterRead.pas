{ context: property-specifier }
program PropertySpecifierAfterRead;
type
  TItem = class
  private
    FSize: Integer;
  public
    property Size: Integer read FSize {|};
  end;
begin
end.
{#has write, default, stored}
