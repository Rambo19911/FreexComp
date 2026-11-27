unit Vault;
interface
type
  TSafe = class
  private
    FCode: Integer;
    procedure Lock;
  protected
    procedure Check;
  public
    procedure Open;
  published
    property Code: Integer read FCode;
  end;
implementation
procedure TSafe.Lock; begin end;
procedure TSafe.Check; begin end;
procedure TSafe.Open; begin end;
end.
