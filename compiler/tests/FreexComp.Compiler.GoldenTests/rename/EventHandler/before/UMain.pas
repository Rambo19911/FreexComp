unit UMain;

{ CMP-1511: an event-handler method: its declaration, its implementation and the handler values of the embedded form. }

interface

type
  TButton = class
  end;

  TMainForm = class
  published
    Button1: TButton;
    procedure Button1Click(Sender: TObject);
    procedure FormCreate(Sender: TObject);
  end;

implementation

{$FORM UMain.form}

procedure TMainForm.Button1Click(Sender: TObject);
begin
  Writeln('click');
end;

procedure TMainForm.FormCreate(Sender: TObject);
begin
  Button1Click(Self);
end;

end.
