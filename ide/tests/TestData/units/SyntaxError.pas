unit SyntaxError;

interface

uses
  Classes, Controls, Forms;

type
  TBrokenForm = class(TForm)
    Button1: TButton
    procedure Button1Click(Sender: TObject);
  end;

var
  BrokenForm: TBrokenForm;

implementation

{$FORM SyntaxError.form}

end.
