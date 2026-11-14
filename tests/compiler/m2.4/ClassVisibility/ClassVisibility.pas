// covers: LANG-610, LANG-611, LANG-612
program ClassVisibility;

{ A descendant in another unit uses protected and strict protected members of TBase through Self (implicit and
  explicit) and through inherited; the base unit's own routine Peek uses private and protected members. }

uses
  VisBase;

type
  TDerived = class(TBase)
  protected
    procedure Bump; override;
  public
    procedure Show;
  end;

procedure TDerived.Bump;
begin
  inherited Bump;
  FShared := FShared + 100;
end;

procedure TDerived.Show;
begin
  Bump;
  Writeln('derived ', FShared, ' ', FFamily, ' ', Self.FFamily, ' ', Hidden);
end;

var
  D: TDerived;
begin
  D := TDerived.Create;
  Peek(D);
  D.Show;
  Peek(D);
  D.Free;
end.
