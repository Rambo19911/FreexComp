{ context: visibility }
program VisibilityPrivateSameUnit;
type
  TSafe = class
  private
    FCode: Integer;
    procedure Lock;
  protected
    procedure Check;
  public
    procedure Open;
  end;
procedure TSafe.Lock; begin end;
procedure TSafe.Check; begin end;
procedure TSafe.Open; begin end;
var
  S: TSafe;
begin
  S.{|}
end.
{#has FCode, Lock, Check, Open}
