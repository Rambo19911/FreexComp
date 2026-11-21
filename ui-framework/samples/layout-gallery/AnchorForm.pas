unit AnchorForm;

interface

uses
  Classes, Controls, Forms, StdCtrls, ExtCtrls;

type
  TAnchorForm = class(TForm)
    TopLeft: TButton;
    TopRight: TButton;
    BottomLeft: TButton;
    BottomRight: TButton;
    Stretch: TPanel;
    Center: TPanel;
    procedure FormCreate(Sender: TObject);
  end;

var
  AnchorForm: TAnchorForm;

implementation

{$FORM AnchorForm.form}

{ the only geometry change of the gallery; AnchorForm.form.steps repeats it for the designer }
procedure TAnchorForm.FormCreate(Sender: TObject);
begin
  ClientWidth := 450;
  ClientHeight := 260;
end;

end.
