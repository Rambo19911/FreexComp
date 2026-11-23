unit WithHandlers;

interface

uses
  Classes, Controls, Forms, StdCtrls;

type
  THandlersForm = class(TForm)
    OkButton: TButton;
    procedure FormCreate(Sender: TObject);
    procedure OkButtonClick(Sender: TObject);
    procedure OkButtonKeyPress(Sender: TObject; var Key: Char);
  private
  public
  end;

var
  HandlersForm: THandlersForm;

implementation

{$FORM WithHandlers.form}

procedure THandlersForm.FormCreate(Sender: TObject);
begin

end;

procedure THandlersForm.OkButtonClick(Sender: TObject);
begin
  Close;
end;

procedure THandlersForm.OkButtonKeyPress(Sender: TObject; var Key: Char);
begin
  // keep
end;

end.
