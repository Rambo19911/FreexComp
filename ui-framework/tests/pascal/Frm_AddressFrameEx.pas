// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
unit Frm_AddressFrameEx;
{ U-160: a frame derived from the frame of Frm_AddressFrame (ui-framework/phase8/form-inheritance.md section 6,
  RTL-1134): its .form starts with "inherited", overrides the frame's Height and TitleLabel's Caption and adds a
  label; a published field only for the new label. The class is not registered: a form finds it through the
  published field of its inline object (RTL-1105). A unit, not a test program (UIF-1200). }

interface

uses
  Classes, Controls, Forms, StdCtrls, Frm_AddressFrame;

type
  TAddressFrameEx = class(TAddressFrame)
  published
    NoteLabel: TLabel;
  end;

implementation

{$FORM Frm_AddressFrameEx.form}

end.
