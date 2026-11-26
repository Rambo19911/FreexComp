# Accessibility

Programs built with FreexComp describe their windows and controls to screen readers, magnifiers, voice
control and test tools through the accessibility interface of Windows. A user who cannot see the screen
hears the name and the kind of each control, and a test tool can find a control by its name and press it.
Nothing has to be switched on: the description is there as soon as such a tool asks for it, and costs
nothing while no tool listens.

## What a control tells
- **Name:** what the control is called, for example `Save` or `Customer name`.
- **Control type:** what kind of control it is, for example button, edit box, list or grid.
- **Description:** a longer help text.
- **Automation id:** the control's `Name` in the form, so a test tool finds it even when the caption is
  translated.
- **State:** enabled, visible, has the focus.

## The three properties
Every control has three properties in the `Misc` category of the Object Inspector:

| Property | Meaning |
|---|---|
| `AccessibleName` | the name a screen reader says; leave it empty to use the rules below |
| `AccessibleDescription` | the help text; empty: the control's `Hint` is used |
| `AccessibleRole` | the control type; `arDefault` keeps the type of the control's class |

Change `AccessibleRole` only when a control is used as something else than its class suggests, for example
a panel that works as a button.

## Where the name comes from
When `AccessibleName` is empty, the name is the first of these that is not empty:
1. the control's own text: the `Caption` of a button, check box, radio button, group box, panel, tab sheet,
   label, tool button or form. The access-key `&` is left out. A tool button without a caption and an image
   use their `Hint` (the part before a `|`).
2. the caption of a label that belongs to the control: a label or static text on the same form whose
   `FocusControl` is the control.

Edit boxes, lists and other controls without a caption of their own therefore need a label with
`FocusControl` set, or an `AccessibleName`. This is also what makes the label's access key move the focus to
the control.

## Controls that bring their own description
Edit boxes, memos, list boxes, combo boxes, check boxes, buttons, tree views, list views, progress bars,
track bars, tab controls and rich edits are Windows controls. Windows itself describes them, including
what can be done with them: text can be read and changed, a check box toggled, a list item selected, a
track bar moved. FreexComp adds the name, description and automation id from the properties above.

## Controls drawn by FreexComp
Controls that FreexComp draws itself (forms, panels, labels, images, tool buttons, grids and frames)
are described by FreexComp:
- **Tool buttons** can be pressed by a tool. A tool button with the style `tbsCheck` also tells whether it
  is down and can be toggled; a screen reader hears when it changes.
- **Grids** (`TStringGrid` and grid classes of your own derived from `TCustomGrid`) tell their number of rows and columns, their header cells, the
  focused cell and the selected cells. Each cell can be read, selected and, when the grid allows editing,
  changed. The text of a cell is its `Cells` value in a string grid. In your own grid class, override
  `GetAccessibleCellText` to give a cell its text.
- A change of a name, of the enabled state or of the controls on a form is reported to a listening tool at
  once.

## Checking your program
Windows contains a screen reader (Narrator, start and stop it with Ctrl+Windows+Enter). Move the focus
through your form with Tab and listen whether every control has a sensible name. A control that is read
only as its kind ("edit", "button") needs a label with `FocusControl` or an `AccessibleName`.
