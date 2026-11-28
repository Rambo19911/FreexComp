unit UMain;

{ CMP-1511: an event-handler method: its declaration, its implementation and the handler values of the embedded form. }

interface

type
  TButton = class
  end;

  TMainForm = class
  published
    Button1: TButton;
    procedure OkClick(Sender: TObject);
    procedure FormCreate(Sender: TObject);
  end;

implementation

{$FORM UMain.form}

procedure TMainForm.OkClick(Sender: TObject);
begin
  Writeln('click');
end;

procedure TMainForm.FormCreate(Sender: TObject);
begin
  OkClick(Self);
end;

end.
