unit Unit1;
interface
uses StdCtrls;
type
  TForm1 = class(TForm)
    {#}Button1: TButton;
    procedure FormCreate(Sender: TObject);
  end;
implementation
procedure TForm1.FormCreate(Sender: TObject);
begin
  {|}Button1.Caption := 'OK';
end;
end.
