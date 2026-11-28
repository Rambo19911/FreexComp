unit UMain;

{ CMP-1511: a component of an embedded form: its published field and its object header. }

interface

type
  TButton = class
  end;

  TMainForm = class
  published
    OkButton: TButton;
    procedure ButtonClick(Sender: TObject);
  end;

implementation

{$FORM UMain.form}

procedure TMainForm.ButtonClick(Sender: TObject);
begin
  Writeln('clicked ', Sender = OkButton);
end;

end.
