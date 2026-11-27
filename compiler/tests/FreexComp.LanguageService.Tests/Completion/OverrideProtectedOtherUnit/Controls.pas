unit Controls;
interface
type
  TControl = class
  private
    procedure Internal; virtual;
  protected
    procedure Changed; virtual;
    procedure Plain;
  public
    procedure Paint; virtual;
  end;
implementation
procedure TControl.Internal; begin end;
procedure TControl.Changed; begin end;
procedure TControl.Plain; begin end;
procedure TControl.Paint; begin end;
end.
