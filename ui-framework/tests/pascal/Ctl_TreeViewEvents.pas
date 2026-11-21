// covers: UIF-832, UIF-833, UIF-834
// needs: desktop
program Ctl_TreeViewEvents;
{ U-089, controls-standard.md section 26 (events). Programmatic selection goes through the native notifications:
  OnChanging (a veto keeps the old selection) then OnChange (UIF-832, UIF-834); Node.Expand / Collapse / Expanded
  fire OnExpanding / OnCollapsing (a veto keeps the state) and OnExpanded / OnCollapsed, only for a real change of a
  node with children (UIF-834); the user's keys go through the same events; the label edit notifications map to
  OnEditing (veto: result 1) and OnEdited (Node.Text := S); no events while the window is created, recreated or
  destroyed (UIF-833); streaming of the properties and the events. }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, CommCtrl, ComCtrls, TestKit;

type
  TWinAccess = class(TWinControl);

  TLog = class
  public
    Text: string;
    Veto, VetoExpand, VetoEdit: Boolean;
    procedure Change(Sender: TObject; Node: TTreeNode);
    procedure Changing(Sender: TObject; Node: TTreeNode; var AllowChange: Boolean);
    procedure Expanding(Sender: TObject; Node: TTreeNode; var AllowExpansion: Boolean);
    procedure Expanded(Sender: TObject; Node: TTreeNode);
    procedure Collapsing(Sender: TObject; Node: TTreeNode; var AllowCollapse: Boolean);
    procedure Collapsed(Sender: TObject; Node: TTreeNode);
    procedure Editing(Sender: TObject; Node: TTreeNode; var AllowEdit: Boolean);
    procedure Edited(Sender: TObject; Node: TTreeNode; var S: string);
  end;

  TTreeForm = class(TForm)
  published
    Tree: TTreeView;
    procedure TreeChange(Sender: TObject; Node: TTreeNode);
    procedure TreeExpanded(Sender: TObject; Node: TTreeNode);
  end;

{$FORM Ctl_TreeViewEvents.form}

var
  Log: TLog;

function NodeName(Node: TTreeNode): string;
begin
  if Node = nil then
    Result := '-'
  else
    Result := Node.Text;
end;

procedure TLog.Change(Sender: TObject; Node: TTreeNode);
begin
  Text := Text + 'change:' + NodeName(Node) + ';';
end;

procedure TLog.Changing(Sender: TObject; Node: TTreeNode; var AllowChange: Boolean);
begin
  Text := Text + 'changing:' + NodeName(Node) + ';';
  AllowChange := not Veto;
end;

procedure TLog.Expanding(Sender: TObject; Node: TTreeNode; var AllowExpansion: Boolean);
begin
  Text := Text + 'expanding:' + NodeName(Node) + ';';
  AllowExpansion := not VetoExpand;
end;

procedure TLog.Expanded(Sender: TObject; Node: TTreeNode);
begin
  Text := Text + 'expanded:' + NodeName(Node) + ';';
end;

procedure TLog.Collapsing(Sender: TObject; Node: TTreeNode; var AllowCollapse: Boolean);
begin
  Text := Text + 'collapsing:' + NodeName(Node) + ';';
  AllowCollapse := not VetoExpand;
end;

procedure TLog.Collapsed(Sender: TObject; Node: TTreeNode);
begin
  Text := Text + 'collapsed:' + NodeName(Node) + ';';
end;

procedure TLog.Editing(Sender: TObject; Node: TTreeNode; var AllowEdit: Boolean);
begin
  Text := Text + 'editing:' + NodeName(Node) + ';';
  AllowEdit := not VetoEdit;
end;

procedure TLog.Edited(Sender: TObject; Node: TTreeNode; var S: string);
begin
  Text := Text + 'edited:' + NodeName(Node) + ':' + S + ';';
  S := UpperCase(S);
end;

procedure TTreeForm.TreeChange(Sender: TObject; Node: TTreeNode);
begin
  Log.Text := Log.Text + 'streamed change;';
end;

procedure TTreeForm.TreeExpanded(Sender: TObject; Node: TTreeNode);
begin
  Log.Text := Log.Text + 'streamed expanded;';
end;

function HasStyle(C: TWinControl; Bits: NativeInt): Boolean;
begin
  Result := (GetWindowLongPtrW(C.Handle, GWL_STYLE) and Bits) = Bits;
end;

procedure Key(C: TWinControl; K: Word);
begin
  SendMessageW(C.Handle, WM_KEYDOWN, K, 1);
  SendMessageW(C.Handle, WM_KEYUP, K, LPARAM($C0000001));
end;

{ the label edit notifications the window sends (Text nil: the edit was cancelled) }
function LabelEdit(T: TTreeView; Code: UINT; N: TTreeNode; Text: PChar): LRESULT;
var
  D: TTVDispInfoW;
begin
  FillChar(D, SizeOf(D), 0);
  D.hdr.hwndFrom := T.Handle;
  D.hdr.code := Code;
  D.item.mask := TVIF_TEXT or TVIF_HANDLE or TVIF_PARAM;
  D.item.hItem := N.ItemId;
  D.item.lParam := LPARAM(Pointer(N));
  D.item.pszText := Text;
  Result := SendMessageW(T.Parent.Handle, WM_NOTIFY, 0, LPARAM(@D));
end;

procedure Run;
var
  F: TForm;
  T: TTreeView;
  TF: TTreeForm;
  R1, R2, A, A1, A2, B, C: TTreeNode;
begin
  Application.Initialize;
  Log := TLog.Create;
  F := TForm.CreateNew(nil);
  try
    F.ClientWidth := 640;
    F.ClientHeight := 400;
    T := TTreeView.Create(F);
    T.SetBounds(8, 8, 300, 300);
    T.OnChange := Log.Change;
    T.OnChanging := Log.Changing;
    T.OnExpanding := Log.Expanding;
    T.OnExpanded := Log.Expanded;
    T.OnCollapsing := Log.Collapsing;
    T.OnCollapsed := Log.Collapsed;
    T.OnEditing := Log.Editing;
    T.OnEdited := Log.Edited;
    R1 := T.Items.Add(nil, 'R1');
    A := T.Items.AddChild(R1, 'A');
    A1 := T.Items.AddChild(A, 'A1');
    A2 := T.Items.AddChild(A, 'A2');
    B := T.Items.AddChild(R1, 'B');
    R2 := T.Items.Add(nil, 'R2');
    C := T.Items.AddChild(R2, 'C');

    Section('without a window');
    R1.Expanded := True;
    A.Expanded := True;
    T.Selected := A2;
    CheckEqS('', Log.Text, 'no events without a window');

    Section('the window is created (UIF-833)');
    T.Parent := F;
    T.HandleNeeded;
    CheckEqS('', Log.Text, 'no events while the state is restored');
    CheckEqB(True, T.Selected = A2, 'selection restored');

    Section('programmatic selection (UIF-834)');
    T.Selected := B;
    CheckEqS('changing:B;change:B;', Log.Text, 'Selected := B');
    CheckEqB(True, T.Selected = B, 'B selected');
    Log.Text := '';
    Log.Veto := True;
    T.Selected := R2;
    CheckEqS('changing:R2;', Log.Text, 'a vetoed change');
    CheckEqB(True, T.Selected = B, 'the selection is kept');
    Log.Veto := False;
    Log.Text := '';
    A1.Selected := True;
    CheckEqS('changing:A1;change:A1;', Log.Text, 'Node.Selected := True');
    Log.Text := '';
    T.Selected := A1;
    CheckEqS('', Log.Text, 'the same node again: no events');
    T.Selected := nil;
    CheckEqS('changing:-;change:-;', Log.Text, 'Selected := nil');
    CheckEqB(True, T.Selected = nil, 'nothing selected');

    Section('expanding and collapsing (UIF-834)');
    Log.Text := '';
    R2.Expand(False);
    CheckEqS('expanding:R2;expanded:R2;', Log.Text, 'Expand');
    CheckEqB(True, R2.Expanded, 'expanded');
    Log.Text := '';
    R2.Expanded := True;
    CheckEqS('', Log.Text, 'already expanded: no events');
    R2.Collapse(False);
    CheckEqS('collapsing:R2;collapsed:R2;', Log.Text, 'Collapse');
    CheckEqB(False, R2.Expanded, 'collapsed');
    Log.Text := '';
    Log.VetoExpand := True;
    R2.Expanded := True;
    CheckEqS('expanding:R2;', Log.Text, 'a vetoed expansion');
    CheckEqB(False, R2.Expanded, 'still collapsed');
    Log.Text := '';
    A.Expanded := False;
    CheckEqS('collapsing:A;', Log.Text, 'a vetoed collapse');
    CheckEqB(True, A.Expanded, 'still expanded');
    Log.VetoExpand := False;
    Log.Text := '';
    C.Expanded := True;
    CheckEqS('', Log.Text, 'a node without children: no events');
    R1.Collapse(True);
    CheckEqS('collapsing:A;collapsed:A;collapsing:R1;collapsed:R1;', Log.Text, 'Collapse(True): the children first');
    Log.Text := '';
    R1.Expand(True);
    CheckEqS('expanding:R1;expanded:R1;expanding:A;expanded:A;', Log.Text, 'Expand(True): the node first');

    Section('the user');
    T.Selected := R2;
    Log.Text := '';
    Key(T, VK_RIGHT);
    CheckEqS('expanding:R2;expanded:R2;', Log.Text, 'Right expands');
    Log.Text := '';
    Key(T, VK_DOWN);
    CheckEqS('changing:C;change:C;', Log.Text, 'Down selects the next node');
    Log.Text := '';
    Key(T, VK_LEFT);
    CheckEqS('changing:R2;change:R2;', Log.Text, 'Left on a leaf selects its parent');
    Log.Text := '';
    Key(T, VK_LEFT);
    CheckEqS('collapsing:R2;collapsed:R2;', Log.Text, 'Left collapses');

    Section('label edit (UIF-832)');
    Log.Text := '';
    CheckEqI(0, LabelEdit(T, TVN_BEGINLABELEDITW, A, nil), 'an allowed edit: 0');
    CheckEqS('editing:A;', Log.Text, 'OnEditing');
    Log.Text := '';
    Log.VetoEdit := True;
    CheckEqI(1, LabelEdit(T, TVN_BEGINLABELEDITW, A, nil), 'a vetoed edit: 1');
    Log.VetoEdit := False;
    Log.Text := '';
    LabelEdit(T, TVN_ENDLABELEDITW, A, 'renamed');
    CheckEqS('edited:A:renamed;', Log.Text, 'OnEdited');
    CheckEqS('RENAMED', A.Text, 'Node.Text := S');
    Log.Text := '';
    LabelEdit(T, TVN_ENDLABELEDITW, A, nil);
    CheckEqS('', Log.Text, 'a cancelled edit: no OnEdited');
    CheckEqS('RENAMED', A.Text, 'Text unchanged');
    CheckEqB(False, T.IsEditing, 'not editing');

    Section('deleting the selected node');
    T.Selected := A2;
    Log.Text := '';
    A2.Delete;
    Check(Pos('change:', Log.Text) > 0, 'the window moves the selection: OnChange');
    Check((T.Selected <> nil) and (T.Selected <> A2), 'another node is selected');

    Section('recreating and destroying (UIF-833)');
    Log.Text := '';
    TWinAccess(T).RecreateWnd;
    CheckEqS('', Log.Text, 'no events while recreating');
    T.Selected := B;
    Log.Text := '';
    T.Free;
    CheckEqS('', Log.Text, 'no events while destroying');

    Section('streaming');
    TF := TTreeForm.Create(nil);
    try
      CheckEqI(25, TF.Tree.Indent, 'Indent');
      CheckEqI(25, SendMessageW(TF.Tree.Handle, TVM_GETINDENT, 0, 0), 'native indent');
      CheckEqB(False, TF.Tree.ShowLines, 'ShowLines False');
      CheckEqB(True, TF.Tree.ReadOnly, 'ReadOnly');
      CheckEqB(False, TF.Tree.HideSelection, 'HideSelection False');
      CheckEqB(False, HasStyle(TF.Tree, TVS_HASLINES or TVS_EDITLABELS), 'native styles');
      Check(HasStyle(TF.Tree, TVS_SHOWSELALWAYS), 'TVS_SHOWSELALWAYS');
      Check(Assigned(TF.Tree.OnChange) and Assigned(TF.Tree.OnExpanded), 'events bound');
      Log.Text := '';
      TF.Tree.Items.AddChild(TF.Tree.Items.Add(nil, 'p'), 'q').Parent.Expand(False);
      TF.Tree.Selected := TF.Tree.Items[1];
      CheckEqS('streamed expanded;streamed change;', Log.Text, 'the streamed handlers run');
    finally
      TF.Free;
    end;
  finally
    F.Free;
    Log.Free;
  end;
end;

begin
  Run;
  Done;
end.
