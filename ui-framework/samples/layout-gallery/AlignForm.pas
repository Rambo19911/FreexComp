unit AlignForm;

interface

uses
  Classes, Controls, Forms, StdCtrls, ExtCtrls, ComCtrls;

type
  TAlignForm = class(TForm)
    ToolBar: TToolBar;
    NewBtn: TToolButton;
    OpenBtn: TToolButton;
    Sep1: TToolButton;
    SaveBtn: TToolButton;
    StatusBar: TStatusBar;
    SidePanel: TPanel;
    Splitter1: TSplitter;
    RightPanel: TPanel;
    Memo1: TMemo;
  end;

var
  AlignForm: TAlignForm;

implementation

{$FORM AlignForm.form}

end.
