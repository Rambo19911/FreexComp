unit VisUnit;

interface

type
  TBase = class
  private
    FSecret: Integer;
  strict private
    FHidden: Integer;
  protected
    FShared: Integer;
  public
    function Sum: Integer;
  end;

implementation

function TBase.Sum: Integer;
begin
  Result := FSecret + FHidden + FShared;
end;

end.
