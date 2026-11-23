unit LfLineEndings;

interface

uses
  Classes, Controls, Forms, StdCtrls;

type
  TLfForm = class(TForm)
    NameEdit: TEdit;
    GreetButton: TButton;
    procedure GreetButtonClick(Sender: TObject);
  private
  public
  end;

var
  LfForm: TLfForm;

implementation

{$FORM LfLineEndings.form}

procedure TLfForm.GreetButtonClick(Sender: TObject);
begin

end;

end.
