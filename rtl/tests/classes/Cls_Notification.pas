// phase: 4
// covers: RTL-453, RTL-454, RTL-458
program Cls_Notification;
{ Notification, FreeNotification, RemoveFreeNotification (classes.md section 8.1): free notifications between
  components of different owners or without owner, both directions, no duplicates, csFreeNotification on both
  (RTL-458); same-owner components rely on the owner's forwarding and get no list entries; a destroyed component
  notifies its free-notification list in list order and is removed from both sides (RTL-453); the base
  Notification removes the component from the list and forwards to every owned component, also while the
  forwarding component is being destroyed (RTL-454). }
uses
  SysUtils, Classes, TestKit;

var
  Log: string;

type
  TRef = class(TComponent)
  public
    Target: TComponent;
  protected
    procedure Notification(AComponent: TComponent; Operation: TOperation); override;
  end;

procedure TRef.Notification(AComponent: TComponent; Operation: TOperation);
begin
  inherited Notification(AComponent, Operation);
  if Operation = opRemove then
  begin
    Log := Log + Name + '<' + AComponent.Name;
    if csDestroying in ComponentState then
      Log := Log + '!';
    Log := Log + ';';
    if AComponent = Target then
      Target := nil;
  end;
end;

function Make(AOwner: TComponent; const AName: string): TRef;
begin
  Result := TRef.Create(AOwner);
  Result.Name := AName;
end;

var
  F1, F2, A, B, C, D, S1, S2: TRef;
begin
  F1 := Make(nil, 'F1');
  F2 := Make(nil, 'F2');
  A := Make(F1, 'A');
  B := Make(F2, 'B');
  C := Make(nil, 'C');
  D := Make(nil, 'D');

  Section('FreeNotification (RTL-458)');
  A.Target := B;
  B.FreeNotification(A);
  B.FreeNotification(A);
  Check(csFreeNotification in A.ComponentState, 'flag on the caller''s argument');
  Check(csFreeNotification in B.ComponentState, 'flag on the receiver');
  C.Target := B;
  B.FreeNotification(C);
  D.Target := B;
  B.FreeNotification(D);
  B.RemoveFreeNotification(D);
  Log := '';
  B.Free;
  CheckEqS('A<B;C<B;B<B!;F2<B;', Log, 'the list in order, once each, then the owner');
  Check(A.Target = nil, 'reference cleared in another owner');
  Check(C.Target = nil, 'reference cleared without owner');
  Check(D.Target <> nil, 'RemoveFreeNotification stops it');
  D.Target := nil;

  Section('both directions');
  B := Make(F2, 'B');
  B.FreeNotification(A);
  Log := '';
  A.Free;
  CheckEqS('B<A;A<A!;F1<A;', Log, 'the other direction notifies too');
  Log := '';
  B.Free;
  CheckEqS('B<B!;F2<B;', Log, 'the freed partner is no longer in the list');

  Section('same owner (RTL-454)');
  S1 := Make(F1, 'S1');
  S2 := Make(F1, 'S2');
  S2.FreeNotification(S1);
  Check(not (csFreeNotification in S1.ComponentState), 'no list entry for a shared owner');
  S1.Target := S2;
  Log := '';
  S2.Free;
  CheckEqS('S1<S2;S2<S2!;F1<S2;', Log, 'the owner forwards to every owned component');
  Check(S1.Target = nil, 'reference cleared through forwarding');
  S2 := Make(F1, 'S2');
  S1.FreeNotification(C);
  Log := '';
  F1.Free;
  CheckEqS('S1<S2!;S2<S2!;F1<S2!;S1<S1!;F1<S1!;C<S1;', Log, 'forwarding while being destroyed');

  Section('no owner at all');
  C.FreeNotification(D);
  Log := '';
  D.Free;
  CheckEqS('C<D;', Log, 'two ownerless components');
  C.Free;
  F2.Free;
  Done;
end.
