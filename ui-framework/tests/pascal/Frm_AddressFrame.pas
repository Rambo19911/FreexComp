// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
unit Frm_AddressFrame;
{ U-154: the frame of Frm_FrameInstances and Frm_FrameDump - a TFrame descendant with its own .form (a label and two
  edits), as the designer creates it, plus the class registration the reader needs to find the class of an inline
  object by its name. A unit, not a test program (UIF-1200). }

interface

uses
  Classes, Controls, Forms, StdCtrls;

type
  TAddressFrame = class(TFrame)
    TitleLabel: TLabel;
    StreetEdit: TEdit;
    CityEdit: TEdit;
  end;

implementation

{$FORM Frm_AddressFrame.form}

initialization
  RegisterClass(TAddressFrame);                              { the reader finds an inline object's class by name }

end.
