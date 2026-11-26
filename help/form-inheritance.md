# Form inheritance

A form can be built on another form of your project. The new form, the *descendant*, starts with everything
its *ancestor* has: its controls, their properties and their event handlers. You add controls of your own
and change values, and the descendant keeps following the ancestor: when you change the ancestor later,
every descendant shows the change. A typical use is a base dialog with a button bar from which every dialog
of the program is made. Frames can be built on other frames in the same way.

## Creating a descendant
Choose File > New > Inherited Form…, or Add New Inherited Form… in the Project menu or in the context menu
of the Project Manager. A list of the forms and frames of the project opens.

1. Choose the ancestor. Type a part of its name or unit to narrow the list.
2. The line below the list shows the name of the new class and unit, for example `TBaseDialog2` in
   `BaseDialog2.pas`.
3. Click OK.

FreexComp adds the new unit with its form file to the project and opens it in the designer. The unit
declares the class with the ancestor as its parent, for example `TBaseDialog2 = class(TBaseDialog)`, and
adds the ancestor's unit to its `uses`. A form inherits from a form, a frame from a frame. Only the forms
and frames of the open project are listed.

The form file of a descendant starts with `inherited` instead of `object` and holds only what the
descendant changes or adds. A descendant can itself be the ancestor of another form, to any depth.

## What is inherited and what you can change
The controls that come from the ancestor are *inherited*. In the Design view each of them shows a small
triangle in its top-left corner; the form itself has none. In the Structure window their names are dimmed,
the root row ends with `(inherits TBaseDialog)`, and the tooltip of an inherited control names the
ancestor it comes from.

You can change inherited controls in the descendant:
- move and resize them within their parent;
- change their properties in the Object Inspector. A changed value is shown in **bold**; values that come
  from the ancestor are shown normally and keep following it;
- place, paste or drop new controls onto the form and into inherited panels and other containers. New
  controls come after the inherited ones in their container.

The form itself (caption, size, colour and the other properties of the root) can be changed in the same
way. Its `Name` is the descendant's own name and can be changed.

## What cannot be done to inherited controls
The ancestor's code works with its controls by their names, and the descendant must contain all of them.
So in a descendant you cannot:
- delete or cut an inherited control;
- rename an inherited control (its `Name` is read-only in the Object Inspector);
- move an inherited control into another parent, or change the order of the inherited controls.

The status bar tells you so when you try; make such changes in the ancestor. Send to Back on a new control
keeps it after the inherited ones, as the status bar says. Copy works: pasted copies of inherited controls
are new, independent controls.

## Revert to Inherited
`Revert to Inherited` in the context menu of the Object Inspector removes your change of one value, so it
follows the ancestor again. On the design surface, `Revert to Inherited` removes all changes of the
selected inherited controls (new controls inside them stay).

## Event handlers
The Events page of an inherited control shows the handler the ancestor assigns, dimmed. Double-click it to
write code of your own for this form: FreexComp adds a method of the same name to the descendant class with
`inherited;` as its first statement, so the ancestor's handler still runs first. Write your code after it,
or remove the call if the ancestor's code should not run. A method you leave untouched is removed when you
save. `Show Inherited Handler` in the context menu of the row opens the ancestor's method.

To use a different method instead, type its name or choose it from the drop-down list; `Revert to
Inherited` brings the ancestor's handler back. Events that have no handler in the ancestor work as on any
form.

## When the ancestor changes
Change the ancestor in its own tab, and every open descendant shows the change when you switch to it. Values
you changed in the descendant stay.

When you rename an ancestor control in the designer (its `Name` in the Object Inspector), every descendant
form and frame of the project is renamed with it, open or not, and keeps its own changes to that control.
Descendants that are not open are saved at once; open ones show the change and are saved with their tab.
One Undo in the ancestor takes the rename back in every file. The rename is refused when a descendant
already has a control of the new name.

If you delete or change the class of an ancestor control that a descendant changed, or rename it by editing
the ancestor's text, the descendant keeps those changes as text it cannot apply: the info bar names them,
and you can edit or remove them in the Text view.

## Frames and inherited forms
Frame instances that the ancestor contains are inherited like any control, and stay frame instances: as on
any form, you cannot add controls into them. A frame built on another frame can be placed like any frame;
its instances show the whole chain.

## Errors
- `E3105`: the form file starts with `inherited`, but no ancestor of the class has a form. Start the file
  with `object`, or derive the class from a form.
- `E3106`: the class inherits the form of an ancestor, but its form file starts with `object`. Start the
  file with `inherited`.
- `F0112` and `F0113` are the designer's warnings for the same two cases, for example when the ancestor's
  unit was removed from the project. The form opens read-only and the info bar shows the reason; correct
  the Text view or the class declaration, or add the ancestor's unit to the project again.
