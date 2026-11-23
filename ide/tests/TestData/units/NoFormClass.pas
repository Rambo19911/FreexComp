unit NoFormClass;

interface

uses
  Classes, Controls, Forms;

type
  TOtherForm = class(TForm)
  end;
  TMissingForm = class;
  TMissingRecord = record
    X: Integer;
  end;

var
  MissingForm: TMissingForm;

implementation

type
  TMissingForm = class(TForm)
  end;

{$FORM NoFormClass.form}

end.
