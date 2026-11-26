unit InheritBase;

interface

uses
  Classes, Controls, Forms, StdCtrls, ExtCtrls;

type
  TBaseForm = class(TForm)
    TitleLabel: TLabel;
    ButtonPanel: TPanel;
    OkButton: TButton;
    CancelButton: TButton;
    procedure OkButtonClick(Sender: TObject);
  private
  public
  end;

var
  BaseForm: TBaseForm;

implementation

{$FORM inherit-base.form}

procedure TBaseForm.OkButtonClick(Sender: TObject);
begin
  Close;
end;

end.
