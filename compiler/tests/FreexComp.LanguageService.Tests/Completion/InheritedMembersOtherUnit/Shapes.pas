unit Shapes;
interface
type
  TShape = class
  private
    FName: string;
    procedure Hidden;
  protected
    procedure Guarded;
  public
    procedure Draw; virtual;
    property Name: string read FName;
  end;
implementation
procedure TShape.Hidden; begin end;
procedure TShape.Guarded; begin end;
procedure TShape.Draw; begin end;
end.
