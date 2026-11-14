unit UMain;

{ A form class whose .form file is embedded with the FORM directive: one published field per child object, one published
  method per event handler (LANG-671). }

interface

type
  TButton = class
  end;

  TMainForm = class
  published
    Button1: TButton;
    procedure FormCreate(Sender: TObject);
    procedure Button1Click(Sender: TObject);
  end;

implementation

{$FORM UMain.form}

procedure TMainForm.FormCreate(Sender: TObject);
begin
end;

procedure TMainForm.Button1Click(Sender: TObject);
begin
end;

end.