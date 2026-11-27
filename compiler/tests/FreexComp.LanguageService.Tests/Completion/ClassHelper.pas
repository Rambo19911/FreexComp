{ context: class-helper }
program ClassHelper;
type
  TDocument = class
    Title: string;
  end;
  TDocumentHelper = class helper for TDocument
    procedure Print;
    function WordCount: Integer;
  end;
procedure TDocumentHelper.Print; begin end;
function TDocumentHelper.WordCount: Integer; begin Result := 0 end;
var
  D: TDocument;
begin
  D.{|}
end.
{#has Title, Print, WordCount, Free}
{#kind Print=Method}
