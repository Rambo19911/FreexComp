// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
{ StdActns - the standard actions (standard-actions.md sections 4 and 5): actions that find their target
  themselves. No class sets a caption, hint, shortcut or image in its constructor: the texts come from the design
  metadata and are stored in the form (UIF-1380). }
unit StdActns;

interface

uses
  Windows, Classes, ActnList, StdCtrls, Dialogs, Forms;

type
  { UIF-1368 to UIF-1370: an action on an edit control. Control = nil: the focused edit (any TCustomEdit) is the
    target, found by the target search; Control <> nil: that control, whether or not it has the focus }
  TEditAction = class(TAction)
  private
    FControl: TCustomEdit;
    procedure SetControl(Value: TCustomEdit);
  protected
    procedure Notification(AComponent: TComponent; Operation: TOperation); override;
    function DirectTarget: TComponent; override;
  public
    constructor Create(AOwner: TComponent); override;
    function HandlesTarget(Target: TObject): Boolean; override;
  published
    property Control: TCustomEdit read FControl write SetControl;
  end;

  { UIF-1371: UpdateTarget sets Enabled, ExecuteTarget calls the operation of the target edit }

  TEditCut = class(TEditAction)
  public
    procedure UpdateTarget(Target: TObject); override;
    procedure ExecuteTarget(Target: TObject); override;
  end;

  TEditCopy = class(TEditAction)
  public
    procedure UpdateTarget(Target: TObject); override;
    procedure ExecuteTarget(Target: TObject); override;
  end;

  TEditPaste = class(TEditAction)
  public
    procedure UpdateTarget(Target: TObject); override;
    procedure ExecuteTarget(Target: TObject); override;
  end;

  TEditSelectAll = class(TEditAction)
  public
    procedure UpdateTarget(Target: TObject); override;
    procedure ExecuteTarget(Target: TObject); override;
  end;

  TEditUndo = class(TEditAction)
  public
    procedure UpdateTarget(Target: TObject); override;
    procedure ExecuteTarget(Target: TObject); override;
  end;

  TEditDelete = class(TEditAction)
  public
    procedure UpdateTarget(Target: TObject); override;
    procedure ExecuteTarget(Target: TObject); override;
  end;

  { UIF-1374, UIF-1375: runs the open dialog it owns (Dialog, set up in code, not streamed). Every component is
    a target; Execute: BeforeExecute, the dialog, then OnAccept or OnCancel }
  TFileOpen = class(TAction)
  private
    FDialog: TOpenDialog;
    FBeforeExecute: TNotifyEvent;
    FOnAccept: TNotifyEvent;
    FOnCancel: TNotifyEvent;
  public
    constructor Create(AOwner: TComponent); override;
    function HandlesTarget(Target: TObject): Boolean; override;
    procedure UpdateTarget(Target: TObject); override;
    procedure ExecuteTarget(Target: TObject); override;
    property Dialog: TOpenDialog read FDialog;
  published
    property BeforeExecute: TNotifyEvent read FBeforeExecute write FBeforeExecute;
    property OnAccept: TNotifyEvent read FOnAccept write FOnAccept;
    property OnCancel: TNotifyEvent read FOnCancel write FOnCancel;
  end;

  { as TFileOpen, with a save dialog }
  TFileSaveAs = class(TAction)
  private
    FDialog: TSaveDialog;
    FBeforeExecute: TNotifyEvent;
    FOnAccept: TNotifyEvent;
    FOnCancel: TNotifyEvent;
  public
    constructor Create(AOwner: TComponent); override;
    function HandlesTarget(Target: TObject): Boolean; override;
    procedure UpdateTarget(Target: TObject); override;
    procedure ExecuteTarget(Target: TObject); override;
    property Dialog: TSaveDialog read FDialog;
  published
    property BeforeExecute: TNotifyEvent read FBeforeExecute write FBeforeExecute;
    property OnAccept: TNotifyEvent read FOnAccept write FOnAccept;
    property OnCancel: TNotifyEvent read FOnCancel write FOnCancel;
  end;

  { UIF-1376: closes the main form (its OnCloseQuery can refuse); without a main form terminates the application }
  TFileExit = class(TAction)
  public
    constructor Create(AOwner: TComponent); override;
    function HandlesTarget(Target: TObject): Boolean; override;
    procedure UpdateTarget(Target: TObject); override;
    procedure ExecuteTarget(Target: TObject); override;
  end;

implementation

type
  TEditAccess = class(TCustomEdit);

{ ---------------------------------------------------------------- TEditAction }

constructor TEditAction.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  DisableIfNoHandler := True;                                { UIF-1361 }
end;

procedure TEditAction.SetControl(Value: TCustomEdit);
begin
  if FControl <> Value then
  begin
    FControl := Value;
    if Value <> nil then
      Value.FreeNotification(Self);
  end;
end;

procedure TEditAction.Notification(AComponent: TComponent; Operation: TOperation);
begin
  inherited Notification(AComponent, Operation);
  if (Operation = opRemove) and (AComponent = FControl) then
    FControl := nil;
end;

function TEditAction.DirectTarget: TComponent;
begin
  Result := FControl;
end;

function TEditAction.HandlesTarget(Target: TObject): Boolean;
begin
  if FControl <> nil then
    Result := Target = FControl
  else
    Result := Target is TCustomEdit;
end;

{ ---------------------------------------------------------------- the six edit actions }

procedure TEditCut.UpdateTarget(Target: TObject);
begin
  Enabled := (TCustomEdit(Target).SelLength > 0) and not TEditAccess(Target).ReadOnly;
end;

procedure TEditCut.ExecuteTarget(Target: TObject);
begin
  TCustomEdit(Target).CutToClipboard;
end;

procedure TEditCopy.UpdateTarget(Target: TObject);
begin
  Enabled := TCustomEdit(Target).SelLength > 0;
end;

procedure TEditCopy.ExecuteTarget(Target: TObject);
begin
  TCustomEdit(Target).CopyToClipboard;
end;

procedure TEditPaste.UpdateTarget(Target: TObject);
begin
  Enabled := not TEditAccess(Target).ReadOnly and IsClipboardFormatAvailable(CF_UNICODETEXT);
end;

procedure TEditPaste.ExecuteTarget(Target: TObject);
begin
  TCustomEdit(Target).PasteFromClipboard;
end;

procedure TEditSelectAll.UpdateTarget(Target: TObject);
begin
  Enabled := TCustomEdit(Target).GetTextLen > 0;
end;

procedure TEditSelectAll.ExecuteTarget(Target: TObject);
begin
  TCustomEdit(Target).SelectAll;
end;

procedure TEditUndo.UpdateTarget(Target: TObject);
begin
  Enabled := TCustomEdit(Target).CanUndo;
end;

procedure TEditUndo.ExecuteTarget(Target: TObject);
begin
  TCustomEdit(Target).Undo;
end;

procedure TEditDelete.UpdateTarget(Target: TObject);
begin
  Enabled := (TCustomEdit(Target).SelLength > 0) and not TEditAccess(Target).ReadOnly;
end;

procedure TEditDelete.ExecuteTarget(Target: TObject);
begin
  TCustomEdit(Target).ClearSelection;
end;

{ ---------------------------------------------------------------- the file actions }

{ UIF-1375: the three events around the dialog of Action }
procedure RunDialog(Action: TObject; Dialog: TOpenDialog; const Before, Accept, Cancel: TNotifyEvent);
begin
  if Assigned(Before) then
    Before(Action);
  if Dialog.Execute then
  begin
    if Assigned(Accept) then
      Accept(Action);
  end
  else if Assigned(Cancel) then
    Cancel(Action);
end;

constructor TFileOpen.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  DisableIfNoHandler := True;
  FDialog := TOpenDialog.Create(Self);
end;

function TFileOpen.HandlesTarget(Target: TObject): Boolean;
begin
  Result := True;
end;

procedure TFileOpen.UpdateTarget(Target: TObject);
begin
  Enabled := True;
end;

procedure TFileOpen.ExecuteTarget(Target: TObject);
begin
  RunDialog(Self, FDialog, FBeforeExecute, FOnAccept, FOnCancel);
end;

constructor TFileSaveAs.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  DisableIfNoHandler := True;
  FDialog := TSaveDialog.Create(Self);
end;

function TFileSaveAs.HandlesTarget(Target: TObject): Boolean;
begin
  Result := True;
end;

procedure TFileSaveAs.UpdateTarget(Target: TObject);
begin
  Enabled := True;
end;

procedure TFileSaveAs.ExecuteTarget(Target: TObject);
begin
  RunDialog(Self, FDialog, FBeforeExecute, FOnAccept, FOnCancel);
end;

constructor TFileExit.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  DisableIfNoHandler := True;
end;

function TFileExit.HandlesTarget(Target: TObject): Boolean;
begin
  Result := True;
end;

procedure TFileExit.UpdateTarget(Target: TObject);
begin
  Enabled := True;
end;

procedure TFileExit.ExecuteTarget(Target: TObject);
begin
  if Application.MainForm <> nil then
    Application.MainForm.Close
  else
    Application.Terminate;
end;

initialization
  RegisterClasses([TEditCut, TEditCopy, TEditPaste, TEditSelectAll, TEditUndo, TEditDelete, TFileOpen,
    TFileSaveAs, TFileExit]);

end.
