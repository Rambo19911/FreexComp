// covers: UIF-1250
program Core_NameSetsCaption;
{ U-119, core.md UIF-1250: renaming a control whose ControlStyle has csSetCaption copies the new name into its
  text (Caption/Text) while the text equals the old name exactly (case-sensitive; an empty old name only with an
  empty text). A changed text is kept; a control that is loading (csLoading, csReading) never copies, so a .form
  keeps its stored captions; a class without csSetCaption never copies. No window handle is created. }
uses
  Windows, Messages, Types, SysUtils, Classes, Controls, Forms, StdCtrls, TestKit;

type
  TPlain = class(TControl)
  public
    property Caption;
  end;

  TNoCopy = class(TControl)
  public
    constructor Create(AOwner: TComponent); override;
    property Caption;
  end;

  TLoadingLabel = class(TLabel)
  public
    procedure StartLoading;
  end;

constructor TNoCopy.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  ControlStyle := ControlStyle - [csSetCaption];
end;

procedure TLoadingLabel.StartLoading;
begin
  Loading;
end;

const
  FormText =
    'object Root: TForm' + #10 +
    '  Caption = ''Stored root''' + #10 +
    '  object Empty: TLabel' + #10 +
    '  end' + #10 +
    '  object Kept: TButton' + #10 +
    '    Caption = ''Stored''' + #10 +
    '  end' + #10 +
    '  object Field: TEdit' + #10 +
    '  end' + #10 +
    'end' + #10;

var
  P: TPlain;
  N: TNoCopy;
  L: TLabel;
  LL: TLoadingLabel;
  B: TButton;
  E: TEdit;
  C: TComboBox;
  F, Root: TForm;
begin
  Section('label follows the name while unchanged');
  L := TLabel.Create(nil);
  try
    Check(csSetCaption in L.ControlStyle, 'label has csSetCaption');
    L.Name := 'Label1';
    CheckEqS('Label1', L.Caption, 'empty old name and empty caption: copied');
    L.Name := 'Label2';
    CheckEqS('Label2', L.Caption, 'caption follows the rename');
    L.Name := 'LABEL2';
    CheckEqS('LABEL2', L.Caption, 'a rename that changes only the case is copied');
    L.Caption := 'Hello';
    L.Name := 'Label3';
    CheckEqS('Hello', L.Caption, 'a changed caption is kept');
    L.Caption := 'label3';
    L.Name := 'Label4';
    CheckEqS('label3', L.Caption, 'comparison is case-sensitive');
    L.Caption := 'Label4';
    L.Name := '';
    CheckEqS('', L.Caption, 'rename to an empty name copies the empty name');
  finally
    L.Free;
  end;

  Section('empty old name');
  P := TPlain.Create(nil);
  try
    P.Caption := 'Text';
    P.Name := 'Plain1';
    CheckEqS('Text', P.Caption, 'empty old name with a non-empty caption: kept');
    P.Caption := 'Plain1';
    P.Name := 'Plain2';
    CheckEqS('Plain2', P.Caption, 'TControl itself copies');
  finally
    P.Free;
  end;

  Section('button and edit');
  B := TButton.Create(nil);
  try
    B.Name := 'Button1';
    CheckEqS('Button1', B.Caption, 'button caption copied');
    B.Name := 'OkButton';
    CheckEqS('OkButton', B.Caption, 'button caption follows');
    B.Caption := '&OK';
    B.Name := 'Ok';
    CheckEqS('&OK', B.Caption, 'changed button caption kept');
  finally
    B.Free;
  end;
  E := TEdit.Create(nil);
  try
    Check(csSetCaption in E.ControlStyle, 'edit has csSetCaption');
    E.Name := 'Edit1';
    CheckEqS('Edit1', E.Text, 'edit text copied');
    E.Name := 'NameEdit';
    CheckEqS('NameEdit', E.Text, 'edit text follows');
    E.Text := 'typed';
    E.Name := 'Edit2';
    CheckEqS('typed', E.Text, 'changed edit text kept');
  finally
    E.Free;
  end;

  Section('form caption');
  F := TForm.CreateNew(nil);
  try
    CheckEqS('', F.Caption, 'new form caption');
    F.Name := 'Form1';
    CheckEqS('Form1', F.Caption, 'form caption copied');
    F.Name := 'MainForm';
    CheckEqS('MainForm', F.Caption, 'form caption follows');
    F.Caption := 'My application';
    F.Name := 'Main';
    CheckEqS('My application', F.Caption, 'changed form caption kept');
  finally
    F.Free;
  end;

  Section('classes without csSetCaption');
  N := TNoCopy.Create(nil);
  try
    N.Name := 'NoCopy1';
    CheckEqS('', N.Caption, 'no copy from an empty name');
    N.Caption := 'NoCopy1';
    N.Name := 'NoCopy2';
    CheckEqS('NoCopy1', N.Caption, 'no copy while the caption equals the old name');
  finally
    N.Free;
  end;
  C := TComboBox.Create(nil);
  try
    Check(not (csSetCaption in C.ControlStyle), 'combo box lacks csSetCaption');
    C.Name := 'Combo1';
    CheckEqS('', C.Text, 'combo box text not copied');
  finally
    C.Free;
  end;

  Section('loading never copies');
  LL := TLoadingLabel.Create(nil);
  try
    LL.Name := 'Loader1';
    CheckEqS('Loader1', LL.Caption, 'copied before loading');
    LL.StartLoading;
    Check(csLoading in LL.ComponentState, 'csLoading set');
    LL.Name := 'Loader2';
    CheckEqS('Loader1', LL.Caption, 'no copy while csLoading');
  finally
    LL.Free;
  end;
  Root := TForm.CreateNew(nil);
  try
    ReadComponentFromText(FormText, Root, 'Core_NameSetsCaption');
    CheckEqS('Root', Root.Name, 'root name read');
    CheckEqS('Stored root', Root.Caption, 'root keeps its stored caption');
    L := Root.FindComponent('Empty') as TLabel;
    CheckEqS('', L.Caption, 'a loaded label without a stored caption stays empty');
    B := Root.FindComponent('Kept') as TButton;
    CheckEqS('Stored', B.Caption, 'a loaded button keeps its stored caption');
    E := Root.FindComponent('Field') as TEdit;
    CheckEqS('', E.Text, 'a loaded edit without stored text stays empty');
    Check(not (csLoading in E.ComponentState), 'loading finished');
    E.Text := 'Field';
    E.Name := 'Field2';
    CheckEqS('Field2', E.Text, 'after loading the copy applies again');
  finally
    Root.Free;
  end;
  Done;
end.
