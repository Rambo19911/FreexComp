{ context: inherited }
program InheritedCall;
type
  TBase = class
    Tag: Integer;
    procedure Run; virtual;
    procedure Reset;
  end;
  TDerived = class(TBase)
    procedure Run; override;
  end;
procedure TBase.Run; begin end;
procedure TBase.Reset; begin end;
procedure TDerived.Run;
begin
  inherited {|}
end;
begin
end.
{#has Run, Reset, Free}
{#kind Run=Method}
{#not Tag}
