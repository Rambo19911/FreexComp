{ context: strict-protected }
program StrictProtectedOtherReceiver;
type
  TBase = class
  strict protected
    FState: Integer;
  public
    procedure Run;
  end;
  TChild = class(TBase)
    procedure Peek(Other: TBase; Sibling: TChild);
  end;
procedure TBase.Run; begin end;
procedure TChild.Peek(Other: TBase; Sibling: TChild);
begin
  Other.{|}
end;
begin
end.
{#has Run}
{#not FState}
