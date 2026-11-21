unit ContainerForm;

interface

uses
  Classes, Controls, Forms, StdCtrls, ExtCtrls, ComCtrls;

type
  TContainerForm = class(TForm)
    Pages: TPageControl;
    Sheet1: TTabSheet;
    Group: TGroupBox;
    Inner: TPanel;
    Scroll: TScrollBox;
    Wide: TPanel;
    Sheet2: TTabSheet;
    List: TListView;
  end;

var
  ContainerForm: TContainerForm;

implementation

{$FORM ContainerForm.form}

end.
