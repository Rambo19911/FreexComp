unit InheritMid;

interface

uses
  Classes, Controls, Forms, StdCtrls, ExtCtrls, InheritBase;

type
  TMidForm = class(TBaseForm)
    HelpButton: TButton;
    NameEdit: TEdit;
  private
  public
  end;

var
  MidForm: TMidForm;

implementation

{$FORM inherit-mid.form}

end.
