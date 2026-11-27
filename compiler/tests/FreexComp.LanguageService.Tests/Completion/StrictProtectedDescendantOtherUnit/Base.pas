unit Base;
interface
type
  TBase = class
  strict private
    FHidden: Integer;
  strict protected
    FState: Integer;
    procedure Notify;
  end;
implementation
procedure TBase.Notify; begin end;
end.
