// covers: LANG-670, LANG-671
program ErrFormMissingField;

{ Form1.form names a handler that is not declared (E3101 method), a component whose field has an incompatible class (E3102)
  and one without a field (E3101 field); Other.form's root class is not declared here (E3103). The positions are in the
  form files. }

type
  TButton = class
  end;

  TEdit = class
  end;

  TForm1 = class
  published
    Button1: TEdit;
    procedure Click(Sender: TObject);
  end;

{$FORM Form1.form}
{$FORM Other.form}

procedure TForm1.Click(Sender: TObject);
begin
end;

begin
end.