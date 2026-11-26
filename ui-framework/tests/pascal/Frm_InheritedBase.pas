// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
unit Frm_InheritedBase;
{ U-160: level 1 of the inherited-form chain of Frm_InheritedChain and Frm_InheritedDump
  (ui-framework/phase8/form-inheritance.md section 2): a dialog with a bottom button panel and a page control, its
  own .form with an "object" root. The handlers and the Loaded override write to the shared log so a test sees
  how often they run. A unit, not a test program (UIF-1200). }

interface

uses
  SysUtils, Classes, Controls, Forms, StdCtrls, ExtCtrls, ComCtrls;

type
  TBaseDialog = class(TForm)
  published
    ButtonPanel: TPanel;
    OkButton: TButton;
    CancelButton: TButton;
    Pages: TPageControl;
    GeneralPage: TTabSheet;
    AdvancedPage: TTabSheet;
    procedure FormCreate(Sender: TObject);
    procedure OkButtonClick(Sender: TObject);
    procedure PageShow(Sender: TObject);
  protected
    procedure Loaded; override;
  end;

var
  Log: string;                                               { handler calls, in order }
  LoadedCount: Integer;                                      { TBaseDialog.Loaded calls }
  CreateCount: Integer;                                      { OnCreate calls }

implementation

{$FORM Frm_InheritedBase.form}

procedure TBaseDialog.FormCreate(Sender: TObject);
begin
  CreateCount := CreateCount + 1;
end;

procedure TBaseDialog.OkButtonClick(Sender: TObject);
begin
  Log := Log + 'base;';
end;

procedure TBaseDialog.PageShow(Sender: TObject);
begin
  Log := Log + TTabSheet(Sender).Name + ' show;';
end;

procedure TBaseDialog.Loaded;
begin
  LoadedCount := LoadedCount + 1;
  inherited Loaded;
end;

end.
