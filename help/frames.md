# Frames

A frame is a part of a form that you design once and use many times: an address block, a row of buttons, a
search bar. You design it like a form, with its controls, their properties and their event handlers. Then
you place it on any number of forms, and on other frames. Every placed copy is an *instance* of the frame.
When you change the frame, every instance changes with it.

## Creating a frame
Choose File > New > Frame, or Add New Frame in the Project menu. FreexComp adds a new unit with its form
file to the project and opens it in the designer. The unit declares the frame class, for example
`TFrame1 = class(TFrame)`; rename it as you like.

Design the frame as you design a form: place controls, set their properties, write event handlers. Save it
like any other unit.

The unit ends with `initialization RegisterClass(TFrame1);`. Keep this line: a form that contains the frame
finds the frame class through it when the program starts.

## Placing an instance
1. Open the form (or the other frame) that should contain the frame.
2. In the Tool Palette, click `Frames…`. A list of the frames of the project opens.
3. Choose a frame, then click on the form where its top-left corner should go.

The instance is named after its class, for example `Frame11` for `TFrame1`. The form's class gets a field of
the frame class, and the frame's unit is added to the form unit's `uses`. Choosing `Frames…` ends a
placement you had started with another palette item.

Only the frames of the open project are listed. A frame cannot contain itself: a frame that would end up
inside itself, directly or through another frame, is not offered and cannot be placed.

## Working with an instance
An instance is an ordinary component of the form: select, move, resize, copy, delete and rename it, and set
its tab order.

Click a control inside the instance to select it. You can move and resize it within its parent and change
its properties in the Object Inspector. These changes belong to this instance only:
- A changed value is shown in **bold**, the values that come from the frame are shown normally.
- `Revert to Inherited` (Object Inspector context menu) removes the change, so the value follows the frame
  again.
- Values you did not change keep following the frame: change a caption in the frame, and every instance
  that kept the caption shows the new one.

## Events of controls in an instance
The Events page of a control inside an instance shows the handler the frame assigns, dimmed. Double-click
it to open the frame's unit at that method.

To handle the event differently on this form, choose a method of the form from the drop-down list or type a
new name: the handler of the form replaces the frame's handler for this instance. `Revert to Inherited`
brings the frame's handler back; a form method no longer used by any event is then offered for removal,
like any handler you clear.

## What cannot be changed in an instance
The structure of an instance comes from the frame. In an instance you cannot:
- delete, cut or rename the frame's controls;
- move a control of the frame into another parent, or change the order of the frame's controls;
- place, paste or drop new controls into the instance or into one of its containers.

Make such changes in the frame itself; the status bar tells you so when you try. The `Name` of a control of
the frame is read-only in the Object Inspector.

When you rename a control in the frame's own designer, every instance of the frame in the project follows:
forms and frames that place it, their descendants and instances inside other frames keep the values they
changed for that control, and references to it from the placing form use the new name. One Undo in the frame
takes the rename back in every file.

## Frames inside frames
A frame can contain instances of other frames. Changes you make in an inner instance are kept in the frame
that contains it, and an instance of that outer frame shows them in turn. Each level can change and revert
the values of the levels below it.
