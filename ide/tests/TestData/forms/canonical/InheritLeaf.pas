unit InheritLeaf;

interface

uses
  Classes, Controls, Forms, StdCtrls, ExtCtrls, InheritMid, AddressFrame;

type
  TLeafForm = class(TMidForm)
    Address: TAddressFrame;
  private
  public
  end;

var
  LeafForm: TLeafForm;

implementation

{$FORM inherit-leaf.form}

end.
