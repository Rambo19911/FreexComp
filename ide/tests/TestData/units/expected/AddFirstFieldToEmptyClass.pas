unit EmptyClass;

interface

uses
  Classes, Controls, Forms;

type
  TEmptyForm = class(TForm)
    Button1: TButton;
  private
  public
  end;

var
  EmptyForm: TEmptyForm;

implementation

{$FORM EmptyClass.form}

end.
