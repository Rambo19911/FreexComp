# Actions

An action is one command of your program kept in one place: its caption, hint, shortcut, its enabled and
checked state, and the code that runs. Menu items, buttons and tool buttons that are linked to the action
show these values and run the same code. Disable the action, and every control linked to it is disabled
with it.

## The action list
Actions belong to an action list (`TActionList`, unit `ActnList`, palette page `Standard`). Drop one on the
form; like every component without a window it is shown in the component tray under the form. Its actions
are created and edited in the Action List Editor (see below).

Every action has these properties and events:

| Property or event | Meaning |
|---|---|
| `Caption` | the text of linked menu items and buttons; `&` marks the access key |
| `Hint` | the hint of linked controls |
| `ShortCut` | the key that runs the action |
| `SecondaryShortCuts` | further keys that run the action, one per line |
| `Enabled`, `Visible`, `Checked` | copied to linked controls |
| `AutoCheck` | `Checked` is switched each time the action runs |
| `ImageIndex` | the image of linked tool buttons and menu items, from the `Images` list of the action list |
| `Category` | a name that groups actions in the editor; it has no effect at run time |
| `OnExecute` | the code of the command |
| `OnUpdate` | code that sets `Enabled`, `Checked` or `Visible` to the current state of the program |
| `OnHint` | runs when the hint of the action is about to be shown |

The action list itself has `OnExecute` and `OnUpdate` events. They run for each of its actions before the
action's own handler, and a handler that sets `Handled` to `True` keeps the action's own handler from running.
Set `State` to `asSuspended` to stop all actions of the list.

## When OnUpdate runs
`OnUpdate` runs whenever the program has nothing else to do, for every action linked to a visible control
of a form or to an item of the form's menu bar, and for actions of hidden tool buttons. It also runs just
before a menu opens, so that its items show the current state, and each time just before the action runs.
A typical pair of handlers:

    procedure TMainForm.SaveActionExecute(Sender: TObject);
    begin
      SaveDocument;
    end;

    procedure TMainForm.SaveActionUpdate(Sender: TObject);
    begin
      SaveAction.Enabled := DocumentModified;
    end;

## Linking controls and menu items
Select the control and choose the action in its `Action` property in the Object Inspector. Menu items,
buttons, check boxes, radio buttons and tool buttons have this property.

- When you link a control, it takes over each value of the action that the control still has at its
  default: a caption that is empty or equal to the control's name, no hint, no shortcut, enabled, visible,
  not checked, no image, no click handler.
  A value you have already set on the control stays.
- A later change of the action reaches every linked control whose value still equals the action's old
  value. A value you changed on the control itself stays independent.
- Clicking the control runs the action. A control that has an `OnClick` handler of its own runs that handler
  instead.
- A menu item shows the shortcut of its action next to its caption.

## Shortcuts
Choose `ShortCut` in the Object Inspector. The form file stores it as a number: `Ctrl+C` is `16451`.

`SecondaryShortCuts` holds more keys for the same action, one key per line, written as text: `Ctrl+Ins`,
`Shift+Del`, `Alt+F7`. Write the modifiers `Ctrl+`, `Alt+` and `Shift+` before the key name. Upper and lower
case do not matter, and spaces around `+` are allowed. Key names are letters, digits, `F1` to `F24`,
`Ins`, `Del`, `Home`, `End`, `PgUp`, `PgDn`, `Left`, `Up`, `Right`, `Down`, `Enter`, `Esc`, `Space`, `Tab`,
`BkSp`, `Num 0` to `Num 9`, `Num +`, `Num -`, `Num *`, `Num /` and `Num .`. A line that is not a key is
ignored. Menus and hints show the first shortcut only. In the form file the list looks like this:

    object EditCopy1: TEditCopy
      Caption = '&Copy'
      Category = 'Edit'
      Hint = 'Copy'
      SecondaryShortCuts = (
        'Ctrl+Ins'
      )
      ShortCut = 16451
    end

When a key is pressed, the form looks for a match in this order: the pop-up menu of the focused control and
of each control around it, the form's menu, the action lists of the form, and then, when the form is not the
main form and no modal form is shown, the same search in the main form. The first item or action that runs
takes the key; the focused control then does not receive it. A disabled action does not take the key, so
the search goes on. Two actions may have the same shortcut; the order decides. A shortcut works even when no
visible control is linked to the action.

## Standard actions
The unit `StdActns` has ready-made actions that do their work without code of yours. An action of this kind
finds the control it works on by itself: the focused control of the active form first, then the form, the
main form and the application. When it finds nothing to work on, the action is disabled, and its shortcut
reaches the focused control as an ordinary key.

### Edit actions
The edit actions work on an edit control: an edit box, a memo or any other control derived from
`TCustomEdit`. Without a value in their `Control` property they work on whichever such control has the
focus. With a control in `Control` they always work on that control, wherever the focus is.

| Action | Enabled when | Does |
|---|---|---|
| `TEditCut` | text is selected and the control is not read-only | cuts the selection to the clipboard |
| `TEditCopy` | text is selected | copies the selection to the clipboard |
| `TEditPaste` | the control is not read-only and the clipboard holds text | pastes the clipboard text |
| `TEditSelectAll` | the control is not empty | selects all text |
| `TEditUndo` | the control can undo its last change | undoes the last change |
| `TEditDelete` | text is selected and the control is not read-only | deletes the selection |

A menu item or button linked to `TEditCopy` is therefore enabled exactly while the focused edit control has
a selection. Ctrl+C runs the action in an edit control with a selection; anywhere else the key goes on to
the focused control.

### File actions

| Action | Does |
|---|---|
| `TFileOpen` | shows an open-file dialog |
| `TFileSaveAs` | shows a save-file dialog |
| `TFileExit` | closes the main form, which ends the program; the form's `OnCloseQuery` can refuse. Without a main form the program ends at once |

The file actions are always enabled. `TFileOpen` and `TFileSaveAs` have three events: `BeforeExecute` runs
before the dialog is shown, `OnAccept` when the user chose a file, and `OnCancel` when the dialog was closed
without one. The dialog is the property `Dialog`. It is not stored in the form file, so set its `Filter`,
`InitialDir` and other properties in code:

    procedure TMainForm.FormCreate(Sender: TObject);
    begin
      FileOpen1.Dialog.Filter := 'Text files (*.txt)|*.txt|All files (*.*)|*.*';
    end;

    procedure TMainForm.FileOpen1Accept(Sender: TObject);
    begin
      Memo1.Lines.LoadFromFile(FileOpen1.Dialog.FileName);
    end;

### Texts of a new standard action
A standard action created in the Action List Editor receives its customary texts. They are written into the
form file like values you typed, so you can change them:

| Action | `Caption` | `ShortCut` | `Category` |
|---|---|---|---|
| `TEditCut` | `Cu&t` | Ctrl+X | `Edit` |
| `TEditCopy` | `&Copy` | Ctrl+C | `Edit` |
| `TEditPaste` | `&Paste` | Ctrl+V | `Edit` |
| `TEditSelectAll` | `Select &All` | Ctrl+A | `Edit` |
| `TEditUndo` | `&Undo` | Ctrl+Z | `Edit` |
| `TEditDelete` | `&Delete` | Del | `Edit` |
| `TFileOpen` | `&Open…` | Ctrl+O | `File` |
| `TFileSaveAs` | `Save &As…` | — | `File` |
| `TFileExit` | `E&xit` | — | `File` |

Give a standard action an `OnExecute` handler, and that handler runs instead of the built-in behaviour.

## The Action List Editor
Double-click the action list on the form, or choose `Action List Editor…` from its context menu. The window
`Edit ActionList1` lists the actions on the left and shows the properties and events of the selected action
on the right, as the Object Inspector does.

| Button | Key | What it does |
|---|---|---|
| `New` | `Insert` | creates a plain action (`TAction`). The arrow of the button (or `Alt+Down`) opens the list of action classes: `Action` first, then the standard actions under the headings `Edit` and `File` |
| `Delete` | `Delete` | deletes the selected actions |
| `Move Up`, `Move Down` | `Ctrl+Up`, `Ctrl+Down` | move the selected action |
| `Close` | `Escape` | closes the editor |

- A new action gets a name from its class (`Action1`, `EditCopy1`), a field in the form's unit and, for a
  standard action, the unit `StdActns` in the `uses` clause of the form's unit.
- Double-click an action, or press `Enter`, to create its default event handler: `OnExecute`, or `OnAccept`
  for `TFileOpen` and `TFileSaveAs`. The editor stays open.
- As soon as one action has a `Category`, the list is grouped under one heading per category. Actions
  without a category come first, under `(No Category)`. `Move Up` and `Move Down` then move an action within
  its group. The grouping is only a view: the form file keeps the actions in their own order.
- There is no Cancel. Every change is applied to the form at once and is one step of Edit > Undo, also after
  the editor is closed.
