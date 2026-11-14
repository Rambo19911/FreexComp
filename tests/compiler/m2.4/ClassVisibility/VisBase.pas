unit VisBase;

{ TBase with members of every visibility; Peek is in the same unit, so private and protected are open to it. }

interface

type
  TBase = class
  private
    FSecret: Integer;
  strict private
    FHidden: Integer;
  protected
    FShared: Integer;
    procedure Bump; virtual;
  strict protected
    FFamily: Integer;
  public
    constructor Create;
    function Hidden: Integer;
  end;

procedure Peek(B: TBase);

implementation

constructor TBase.Create;
begin
  inherited Create;
  FSecret := 1;
  FHidden := 2;
  FShared := 3;
  FFamily := 4;
end;

procedure TBase.Bump;
begin
  FShared := FShared + 1;
end;

function TBase.Hidden: Integer;
begin
  Result := FHidden;
end;

procedure Peek(B: TBase);
begin
  Writeln('peek ', B.FSecret, ' ', B.FShared);
  B.Bump;
  B.FSecret := B.FSecret + 10;
end;

end.
