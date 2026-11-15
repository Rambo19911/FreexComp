// phase: 4
// covers: RTL-440, RTL-441
program Cls_Collection;
{ TCollectionItem, TCollection, TOwnedCollection (classes.md section 7): Add/Insert/Delete/Clear, IDs unique and never
  reused, Index moves, Collection := another collection, DisplayName, GetNamePath, FindItemID, Assign, Owner;
  Update(nil) after add/delete/move and Update(Item) after Item.Changed(False), coalesced into one Update(nil) at the
  outermost EndUpdate (RTL-440); Notify actions; Clear and Destroy free last to first; bad index raises EListError
  (RTL-441). }
uses
  SysUtils, Classes, TestKit;

var
  Log: string;

type
  TThing = class(TCollectionItem)
  private
    FValue: Integer;
    procedure SetValue(V: Integer);
  public
    destructor Destroy; override;
    procedure Assign(Source: TPersistent); override;
    property Value: Integer read FValue write SetValue;
  end;

  TNamedThing = class(TThing)
  protected
    function GetDisplayName: string; override;
  end;

  TThings = class(TCollection)
  protected
    procedure Update(Item: TCollectionItem); override;
    procedure Notify(Item: TCollectionItem; Action: TCollectionNotification); override;
  end;

  TOwnerThing = class(TPersistent)
  public
    function GetNamePath: string; override;
  end;

destructor TThing.Destroy;
begin
  Log := Log + 'free' + IntToStr(FValue) + ';';
  inherited Destroy;
end;

procedure TThing.SetValue(V: Integer);
begin
  FValue := V;
  Changed(False);
end;

procedure TThing.Assign(Source: TPersistent);
begin
  if Source is TThing then
    FValue := TThing(Source).FValue
  else
    inherited Assign(Source);
end;

function TNamedThing.GetDisplayName: string;
begin
  Result := 'thing ' + IntToStr(Value);
end;

procedure TThings.Update(Item: TCollectionItem);
begin
  if Item = nil then
    Log := Log + 'upd(nil);'
  else
    Log := Log + 'upd(' + IntToStr(TThing(Item).Value) + ');';
end;

procedure TThings.Notify(Item: TCollectionItem; Action: TCollectionNotification);
const
  Names: array[TCollectionNotification] of string = ('added', 'extracting', 'deleting');
begin
  Log := Log + Names[Action] + ';';
end;

function TOwnerThing.GetNamePath: string;
begin
  Result := 'Owner1';
end;

function Values(C: TCollection): string;
var
  I: Integer;
begin
  Result := '';
  for I := 0 to C.Count - 1 do
    Result := Result + IntToStr(TThing(C.Items[I]).Value) + '/' + IntToStr(C.Items[I].ID) + ' ';
end;

function IndexError(C: TCollection; Kind, N: Integer): string;
var
  Item: TCollectionItem;
begin
  Result := '';
  try
    case Kind of
      0: Item := C.Items[N];
      1: C.Delete(N);
      2: C.Insert(N);
    end;
  except
    on E: EListError do
      Result := E.Message;
  end;
end;

var
  C, D: TThings;
  O: TOwnedCollection;
  OwnerObj: TOwnerThing;
  T: TThing;
  I: Integer;
begin
  C := TThings.Create(TThing);
  Section('add and update (RTL-440)');
  CheckEqI(0, C.Count, 'empty');
  Check(C.ItemClass = TThing, 'ItemClass');
  T := TThing(C.Add);
  CheckEqS('added;upd(nil);', Log, 'Add notifies and updates');
  Check(T.Collection = C, 'item collection');
  Log := '';
  T.Value := 10;
  CheckEqS('upd(10);', Log, 'Changed(False) updates the item');
  for I := 2 to 4 do
    TThing(C.Add).Value := I * 10;
  CheckEqS('10/0 20/1 30/2 40/3 ', Values(C), 'values and IDs');
  CheckEqI(2, C.Items[2].Index, 'Index');

  Section('update coalescing');
  Log := '';
  C.BeginUpdate;
  C.BeginUpdate;
  TThing(C.Items[0]).Value := 11;
  C.Add;
  C.EndUpdate;
  CheckEqS('added;', Log, 'nothing while updating');
  C.EndUpdate;
  CheckEqS('added;upd(nil);', Log, 'one Update(nil) at the outermost EndUpdate');

  Section('delete, insert, move');
  Log := '';
  C.Delete(4);
  CheckEqS('deleting;free0;extracting;upd(nil);', Log, 'Delete');
  CheckEqS('11/0 20/1 30/2 40/3 ', Values(C), 'after Delete');
  T := TThing(C.Insert(1));
  T.Value := 15;
  CheckEqS('11/0 15/5 20/1 30/2 40/3 ', Values(C), 'Insert; IDs are never reused');
  Log := '';
  T.Index := 3;
  CheckEqS('11/0 20/1 30/2 15/5 40/3 ', Values(C), 'Index moves');
  CheckEqS('upd(nil);', Log, 'a move updates');
  Check(C.FindItemID(5) = T, 'FindItemID');
  Check(C.FindItemID(4) = nil, 'FindItemID of a deleted item');

  Section('collection change');
  D := TThings.Create(TThing);
  Log := '';
  T.Collection := D;
  CheckEqS('extracting;upd(nil);added;upd(nil);', Log, 'moved to another collection');
  CheckEqI(4, C.Count, 'source count');
  CheckEqI(1, D.Count, 'target count');
  CheckEqI(0, T.ID, 'new ID in the target');
  T.Free;
  CheckEqI(0, D.Count, 'Free removes the item');

  Section('names');
  CheckEqS('TThing', C.Items[0].DisplayName, 'base DisplayName');
  CheckEqS('TThings[2]', C.Items[2].GetNamePath, 'GetNamePath without owner');
  OwnerObj := TOwnerThing.Create;
  O := TOwnedCollection.Create(OwnerObj, TNamedThing);
  Check(O.Owner = OwnerObj, 'Owner');
  TThing(O.Add).Value := 7;
  CheckEqS('thing 7', O.Items[0].DisplayName, 'overridden DisplayName');
  CheckEqS('Owner1.TOwnedCollection[0]', O.Items[0].GetNamePath, 'GetNamePath with owner');
  Check(C.Owner = nil, 'no owner');

  Section('assign');
  Log := '';
  D.Add;
  D.Assign(C);
  CheckEqS('11/2 20/3 30/4 40/5 ', Values(D), 'Assign copies through Add.Assign');
  Check(Pos('free0;', Log) > 0, 'Assign clears first');

  Section('index errors (RTL-441)');
  CheckEqS('List index out of bounds (4)', IndexError(C, 0, 4), 'Items');
  CheckEqS('List index out of bounds (-1)', IndexError(C, 1, -1), 'Delete');
  CheckEqS('List index out of bounds (5)', IndexError(C, 2, 5), 'Insert');

  Section('clear and destroy');
  Log := '';
  C.Clear;
  CheckEqS('deleting;free40;extracting;deleting;free30;extracting;deleting;free20;extracting;deleting;free11;extracting;upd(nil);',
    Log, 'Clear frees last to first in one update');
  Log := '';
  D.Free;
  CheckEqS('deleting;free40;extracting;deleting;free30;extracting;deleting;free20;extracting;deleting;free11;extracting;',
    Log, 'Destroy frees last to first');
  C.Free;
  O.Free;
  OwnerObj.Free;
  Done;
end.
