{ context: strict-protected }
program StrictProtectedOtherClassSameUnit;
type
  TBase = class
  strict protected
    FState: Integer;
    procedure Notify;
  protected
    FPlain: Integer;
  end;
  TObserver = class
    procedure Watch(B: TBase);
  end;
procedure TBase.Notify; begin end;
procedure TObserver.Watch(B: TBase);
begin
  B.{|}
end;
begin
end.
{#has FPlain, Free}
{#not FState, Notify}
