unit ExplicitPublished;

interface

uses
  Classes, Controls, Forms, StdCtrls;

type
  TPublishedForm = class(TForm)
    procedure OkButtonClick(Sender: TObject);
  private
    FCount: Integer;
    procedure Helper;
  published
    procedure CancelButtonClick(Sender: TObject);
  public
    function Count: Integer;
  end;

var
  PublishedForm: TPublishedForm;

implementation

{$FORM ExplicitPublished.form}

procedure TPublishedForm.OkButtonClick(Sender: TObject);
begin
end;

procedure TPublishedForm.Helper;
begin
end;

procedure TPublishedForm.CancelButtonClick(Sender: TObject);
begin
end;

function TPublishedForm.Count: Integer;
begin
  Result := FCount;
end;

end.
