unit MainForm;

interface

uses
  Classes, Controls, Forms, StdCtrls;

type
  TStartForm = class(TForm)
    NameEdit: TEdit;
    GreetButton: TButton;
    procedure GreetButtonClick(Sender: TObject);
  private
  public
  end;

var
  StartForm: TStartForm;

implementation

{$FORM MainForm.form}

procedure TStartForm.GreetButtonClick(Sender: TObject);
begin

end;

end.
