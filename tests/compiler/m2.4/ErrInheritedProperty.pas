// covers: LANG-610, LANG-624, LANG-641
program ErrInheritedProperty;

{ inherited Prop (C-270) keeps the rules of the ancestor's property: visibility (E2610), read-only (E2633), write-only
  (E2632), an abstract accessor cannot be called statically (E2626); a name no ancestor declares stays E2003. }

type
  TBase = class
  strict private
    FHidden: Integer;
    property Hidden: Integer read FHidden write FHidden;
  protected
    FCount: Integer;
    function GetShape: Integer; virtual; abstract;
    procedure SetShape(V: Integer); virtual; abstract;
  public
    property Count: Integer read FCount;
    property Sink: Integer write FCount;
    property Shape: Integer read GetShape write SetShape;
  end;

  TDerived = class(TBase)
  public
    procedure Run;
  end;

procedure TDerived.Run;
var
  N: Integer;
begin
  N := inherited Hidden;
  inherited Count := 1;
  N := inherited Sink;
  N := inherited Shape;
  inherited Shape := 2;
  N := inherited Missing;
  inherited Absent := N;
end;

begin
end.
