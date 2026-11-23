unit MainForm;

interface

uses
  Classes, Controls, Forms, StdCtrls;

type
  TMainForm = class(TForm)
    NameEdit: TEdit;
    GreetButton: TButton;
    procedure GreetButtonClick(Sender: TObject);
    procedure NameEditChange(Sender: TObject);
  private
  public
  end;

var
  MainForm: TMainForm;

implementation

{$FORM MainForm.form}

procedure TMainForm.GreetButtonClick(Sender: TObject);
begin

end;

procedure TMainForm.NameEditChange(Sender: TObject);
begin
  
end;

end.
