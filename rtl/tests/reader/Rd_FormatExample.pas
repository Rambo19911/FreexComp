// phase: 4
// covers: RTL-480, RTL-495, RTL-496, RTL-499
program Rd_FormatExample;
{ The example of form-format.md section 3, byte for byte, loads into stand-in classes TMainForm / TEdit / TButton
  declared here; every property value is asserted, the events are bound to the form's methods, the published fields
  receive the children, and Loaded runs children first. }
uses
  SysUtils, Classes, TestKit;

var
  Log: string;

type
  TControlX = class(TComponent)
  private
    FLeft, FTop, FWidth, FHeight, FTabOrder: Integer;
    FCaption, FText: string;
    FOnClick: TNotifyEvent;
  protected
    procedure Loaded; override;
  published
    property Left: Integer read FLeft write FLeft;
    property Top: Integer read FTop write FTop;
    property Width: Integer read FWidth write FWidth;
    property Height: Integer read FHeight write FHeight;
    property TabOrder: Integer read FTabOrder write FTabOrder;
    property Caption: string read FCaption write FCaption;
    property Text: string read FText write FText;
    property OnClick: TNotifyEvent read FOnClick write FOnClick;
  end;

  TEdit = class(TControlX)
  end;

  TButton = class(TControlX)
  end;

  TMainForm = class(TComponent)
  private
    FLeft, FTop, FClientWidth, FClientHeight, FPixelsPerInch: Integer;
    FCaption: string;
    FOnCreate: TNotifyEvent;
  protected
    procedure Loaded; override;
  published
    NameEdit: TEdit;
    GreetButton: TButton;
    procedure FormCreate(Sender: TObject);
    procedure GreetButtonClick(Sender: TObject);
    property Left: Integer read FLeft write FLeft;
    property Top: Integer read FTop write FTop;
    property Caption: string read FCaption write FCaption;
    property ClientWidth: Integer read FClientWidth write FClientWidth;
    property ClientHeight: Integer read FClientHeight write FClientHeight;
    property PixelsPerInch: Integer read FPixelsPerInch write FPixelsPerInch;
    property OnCreate: TNotifyEvent read FOnCreate write FOnCreate;
  end;

procedure TControlX.Loaded;
begin
  inherited Loaded;
  Log := Log + 'loaded ' + Name + ';';
end;

procedure TMainForm.Loaded;
begin
  inherited Loaded;
  Log := Log + 'loaded ' + Name + ';';
end;

procedure TMainForm.FormCreate(Sender: TObject);
begin
  Log := Log + 'FormCreate;';
end;

procedure TMainForm.GreetButtonClick(Sender: TObject);
begin
  Log := Log + 'GreetButtonClick ' + TComponent(Sender).Name + ';';
end;

const
  Example =
    'object MainForm: TMainForm'#10 +
    '  Left = 200'#10 +
    '  Top = 120'#10 +
    '  Caption = ''Hello'''#10 +
    '  ClientWidth = 400'#10 +
    '  ClientHeight = 240'#10 +
    '  PixelsPerInch = 96'#10 +
    '  OnCreate = FormCreate'#10 +
    '  object NameEdit: TEdit'#10 +
    '    Left = 16'#10 +
    '    Top = 16'#10 +
    '    Width = 200'#10 +
    '    Height = 23'#10 +
    '    TabOrder = 0'#10 +
    '  end'#10 +
    '  object GreetButton: TButton'#10 +
    '    Left = 232'#10 +
    '    Top = 15'#10 +
    '    Width = 75'#10 +
    '    Height = 25'#10 +
    '    Caption = ''&Greet'''#10 +
    '    TabOrder = 1'#10 +
    '    OnClick = GreetButtonClick'#10 +
    '  end'#10 +
    'end'#10;

var
  F: TMainForm;
begin
  RegisterClasses([TEdit, TButton]);
  F := TMainForm.Create(nil);
  ReadComponentFromText(Example, F, 'MainForm.form');

  Section('form');
  CheckEqS('MainForm', F.Name, 'Name');
  CheckEqI(200, F.Left, 'Left');
  CheckEqI(120, F.Top, 'Top');
  CheckEqS('Hello', F.Caption, 'Caption');
  CheckEqI(400, F.ClientWidth, 'ClientWidth');
  CheckEqI(240, F.ClientHeight, 'ClientHeight');
  CheckEqI(96, F.PixelsPerInch, 'PixelsPerInch');
  CheckEqI(2, F.ComponentCount, 'two children');

  Section('NameEdit');
  Check(F.NameEdit <> nil, 'field bound');
  Check(F.NameEdit.Owner = F, 'owned by the form');
  CheckEqI(0, F.NameEdit.ComponentIndex, 'first child');
  CheckEqI(16, F.NameEdit.Left, 'Left');
  CheckEqI(16, F.NameEdit.Top, 'Top');
  CheckEqI(200, F.NameEdit.Width, 'Width');
  CheckEqI(23, F.NameEdit.Height, 'Height');
  CheckEqI(0, F.NameEdit.TabOrder, 'TabOrder');
  CheckEqS('', F.NameEdit.Text, 'Text absent (default)');
  Check(not Assigned(F.NameEdit.OnClick), 'no OnClick');

  Section('GreetButton');
  Check(F.GreetButton <> nil, 'field bound');
  CheckEqI(1, F.GreetButton.ComponentIndex, 'second child');
  CheckEqI(232, F.GreetButton.Left, 'Left');
  CheckEqI(15, F.GreetButton.Top, 'Top');
  CheckEqI(75, F.GreetButton.Width, 'Width');
  CheckEqI(25, F.GreetButton.Height, 'Height');
  CheckEqS('&Greet', F.GreetButton.Caption, 'Caption');
  CheckEqI(1, F.GreetButton.TabOrder, 'TabOrder');

  Section('events and order');
  CheckEqS('loaded GreetButton;loaded NameEdit;loaded MainForm;', Log, 'Loaded children first, root last');
  Log := '';
  F.OnCreate(F);
  F.GreetButton.OnClick(F.GreetButton);
  CheckEqS('FormCreate;GreetButtonClick GreetButton;', Log, 'events bound to the form''s methods');
  F.Free;
  Done;
end.
