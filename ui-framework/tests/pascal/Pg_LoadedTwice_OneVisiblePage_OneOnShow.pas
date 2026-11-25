// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
// covers: UIF-771
// phase: 8
program Pg_LoadedTwice_OneVisiblePage_OneOnShow;
{ U-158 (found by R-242, classes.md RTL-1097): a page control inside a frame, reached by an override block of a
  frame instance, gets a second Loaded from the outer reader. controls-standard.md UIF-771: the page made visible by
  the first Loaded is the old page of the second, so a block that names another ActivePage switches pages (the old
  one is hidden with OnHide, the new one shown with one OnShow) and a block that leaves ActivePage alone changes
  nothing (no second OnShow); an instance without a block for the page control and a later switch in code behave as
  before. The page events are methods of the frame, bound in the frame's own file; the log names the instance (an
  empty name while the frame's own file is read inside the constructor). Needs no desktop: no form is shown. }
uses
  Windows, SysUtils, Classes, Controls, Forms, ComCtrls, TestKit;

type
  TTestFrame = class(TScrollBox)
  public
    constructor Create(AOwner: TComponent); override;
  end;

  TPageFrame = class(TTestFrame)
  published
    PC: TPageControl;
    Page1: TTabSheet;
    Page2: TTabSheet;
    Page3: TTabSheet;
    procedure PageShow(Sender: TObject);
    procedure PageHide(Sender: TObject);
  end;

  TPageForm = class(TForm)
  published
    Keep: TPageFrame;
    Switch: TPageFrame;
    Plain: TPageFrame;
  end;

{$FORM Pg_LoadedTwice_PageFrame.form}
{$FORM Pg_LoadedTwice_OneVisiblePage_OneOnShow.form}

var
  Log: string;

constructor TTestFrame.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  if ClassType <> TTestFrame then
    if not InitInheritedComponent(Self, TTestFrame, AOwner = nil) then
      raise EResNotFound.CreateFmt('Resource %s not found', [ClassName]);
end;

function Who(Sender: TObject): string;
var
  Page: TTabSheet;
begin
  Page := Sender as TTabSheet;
  if Page.Owner.Name = '' then
    Result := '?.' + Page.Name
  else
    Result := Page.Owner.Name + '.' + Page.Name;
end;

procedure TPageFrame.PageShow(Sender: TObject);
begin
  Log := Log + Who(Sender) + ' show;';
end;

procedure TPageFrame.PageHide(Sender: TObject);
begin
  Log := Log + Who(Sender) + ' hide;';
end;

function VisiblePages(PC: TPageControl): string;
var
  I: Integer;
begin
  Result := '';
  for I := 0 to PC.PageCount - 1 do
    if PC.Pages[I].Visible then
      Result := Result + PC.Pages[I].Name + ';';
end;

var
  F: TPageForm;
begin
  RegisterClasses([TPageFrame]);

  Section('a frame in code');
  Log := '';
  F := TPageForm.CreateNew(nil);
  with TPageFrame.Create(F) do
  begin
    CheckEqS('Page2', PC.ActivePage.Name, 'the frame''s ActivePage');
    CheckEqS('Page2;', VisiblePages(PC), 'one visible page');
  end;
  CheckEqS('?.Page2 show;', Log, 'one OnShow');
  F.Free;

  Section('three instances, two with a block for the page control (UIF-771)');
  Log := '';
  F := TPageForm.Create(nil);
  CheckEqS('?.Page2 show;?.Page2 show;?.Page2 show;Switch.Page2 hide;Switch.Page3 show;', Log,
    'each frame shows Page2 once; the switching block hides it and shows Page3 once');
  CheckEqS('Page2', F.Keep.PC.ActivePage.Name, 'Keep: ActivePage');
  CheckEqS('Page2;', VisiblePages(F.Keep.PC), 'Keep: one visible page');
  CheckEqI(1, F.Keep.PC.Tag, 'Keep: the block was applied');
  CheckEqS('Page3', F.Switch.PC.ActivePage.Name, 'Switch: ActivePage of the block');
  CheckEqS('Page3;', VisiblePages(F.Switch.PC), 'Switch: one visible page');
  CheckEqS('Third', F.Switch.Page3.Caption, 'Switch: the nested block was applied');
  CheckEqS('Page2', F.Plain.PC.ActivePage.Name, 'Plain: ActivePage');
  CheckEqS('Page2;', VisiblePages(F.Plain.PC), 'Plain: one visible page');

  Section('switching in code afterwards');
  Log := '';
  F.Keep.PC.ActivePage := F.Keep.Page1;
  CheckEqS('Keep.Page2 hide;Keep.Page1 show;', Log, 'Keep: the switch');
  CheckEqS('Page1;', VisiblePages(F.Keep.PC), 'Keep: one visible page');
  Log := '';
  F.Switch.PC.ActivePageIndex := 0;
  CheckEqS('Switch.Page3 hide;Switch.Page1 show;', Log, 'Switch: the switch leaves from Page3');
  CheckEqS('Page1;', VisiblePages(F.Switch.PC), 'Switch: one visible page');
  F.Free;
  Done;
end.
