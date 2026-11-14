// phase: 4
// covers: RTL-418, RTL-426
program Cls_StringList;
{ TStringList (classes.md section 5.2): Sorted mode (sorting on set, Add at the sorted position, Insert/Put/Exchange/
  Move raising "Operation not allowed on sorted list"), Duplicates (ignore returns the existing index, error raises,
  accept inserts after equal items), CaseSensitive (re-sorts), Find (lowest match, insertion position, RTL-426),
  IndexOf via Find when sorted, stable Sort and CustomSort (RTL-418), OnChanging/OnChange per modification and once
  per outermost update, OwnsObjects freeing on Delete/Clear/overwrite/destruction, the three constructors. }
uses
  SysUtils, Classes, TestKit;

type
  TTracked = class
    destructor Destroy; override;
  end;

  TEvents = class
    Log: string;
    procedure Changing(Sender: TObject);
    procedure Changed(Sender: TObject);
  end;

var
  Freed: Integer;

destructor TTracked.Destroy;
begin
  Freed := Freed + 1;
  inherited Destroy;
end;

procedure TEvents.Changing(Sender: TObject); begin Log := Log + '<'; end;
procedure TEvents.Changed(Sender: TObject); begin Log := Log + '>'; end;

function Lines(L: TStrings): string;
var
  I: Integer;
begin
  Result := '';
  for I := 0 to L.Count - 1 do
    Result := Result + L[I] + ' ';
end;

function ByLength(List: TStringList; Index1, Index2: Integer): Integer;
begin
  Result := Length(List[Index1]) - Length(List[Index2]);
end;

function SortedError(L: TStringList; Kind: Integer): string;
begin
  Result := '';
  try
    case Kind of
      0: L.Insert(0, 'x');
      1: L[0] := 'x';
      2: L.Exchange(0, 1);
      3: L.Move(0, 1);
      4: L.Add('PEAR');
    end;
  except
    on E: EStringListError do
      Result := E.Message;
  end;
end;

var
  L: TStringList;
  E: TEvents;
  I: Integer;
begin
  Section('sorted mode');
  L := TStringList.Create;
  L.Add('pear');
  L.Add('Apple');
  L.Add('fig');
  L.Sorted := True;
  CheckEqS('Apple fig pear ', Lines(L), 'setting Sorted sorts (case-insensitive)');
  CheckEqI(2, L.Add('kiwi'), 'Add returns the sorted position');
  CheckEqS('Apple fig kiwi pear ', Lines(L), 'inserted in order');
  CheckEqS('Operation not allowed on sorted list', SortedError(L, 0), 'Insert');
  CheckEqS('Operation not allowed on sorted list', SortedError(L, 1), 'Put');
  CheckEqS('Operation not allowed on sorted list', SortedError(L, 2), 'Exchange');
  CheckEqS('Operation not allowed on sorted list', SortedError(L, 3), 'Move');

  Section('duplicates');
  CheckEqI(1, L.Add('FIG'), 'dupIgnore returns the existing index');
  CheckEqI(4, L.Count, 'nothing added');
  L.Duplicates := dupError;
  CheckEqS('String list does not allow duplicates', SortedError(L, 4), 'dupError');
  CheckEqI(4, L.Count, 'nothing added on error');
  L.Duplicates := dupAccept;
  L.AddObject('Fig', L);
  L.AddObject('FIG', nil);
  CheckEqS('Apple fig Fig FIG kiwi pear ', Lines(L), 'dupAccept inserts after equal items');
  Check(L.Objects[2] = L, 'object of the accepted duplicate');

  Section('Find and IndexOf (RTL-426)');
  I := -1;
  CheckEqB(True, L.Find('fig', I), 'found');
  CheckEqI(1, I, 'lowest matching index');
  CheckEqB(False, L.Find('grape', I), 'absent');
  CheckEqI(4, I, 'insertion position');
  CheckEqB(False, L.Find('zzz', I), 'past the end');
  CheckEqI(6, I, 'insertion at Count');
  CheckEqI(4, L.IndexOf('KIWI'), 'IndexOf sorted');
  CheckEqI(-1, L.IndexOf('grape'), 'IndexOf sorted absent');

  Section('case sensitivity');
  L.CaseSensitive := True;
  CheckEqS('Apple FIG Fig fig kiwi pear ', Lines(L), 'changing CaseSensitive re-sorts');
  CheckEqI(-1, L.IndexOf('KIWI'), 'case-sensitive IndexOf');
  L.Free;

  Section('stable sorts (RTL-418)');
  L := TStringList.Create;
  L.CommaText := 'b2,A1,b1,a2,B3,a3';
  L.Sort;
  CheckEqS('A1 a2 a3 b1 b2 B3 ', Lines(L), 'Sort');
  L.CommaText := 'ccc,a,bb,dd,e,fff';
  L.CustomSort(ByLength);
  CheckEqS('a e bb dd ccc fff ', Lines(L), 'CustomSort keeps equal lengths in order');
  L.Free;

  Section('events');
  E := TEvents.Create;
  L := TStringList.Create;
  L.OnChanging := E.Changing;
  L.OnChange := E.Changed;
  L.Add('a');
  L.Add('b');
  L[0] := 'c';
  L.Delete(1);
  CheckEqS('<><><><>', E.Log, 'one pair per modification');
  E.Log := '';
  L.BeginUpdate;
  L.Add('x');
  L.Add('y');
  L.BeginUpdate;
  L.Clear;
  L.EndUpdate;
  CheckEqS('<', E.Log, 'OnChanging at the outermost BeginUpdate');
  L.EndUpdate;
  CheckEqS('<>', E.Log, 'OnChange once at the outermost EndUpdate');
  E.Log := '';
  L.Text := 'p'#10'q'#10'r';
  CheckEqS('<>', E.Log, 'Text set is one change');
  L.Free;
  E.Free;

  Section('OwnsObjects');
  Freed := 0;
  L := TStringList.Create(True);
  CheckEqB(True, L.OwnsObjects, 'constructor flag');
  L.AddObject('a', TTracked.Create);
  L.AddObject('b', TTracked.Create);
  L.AddObject('c', TTracked.Create);
  L.AddObject('d', TTracked.Create);
  L.Delete(0);
  CheckEqI(1, Freed, 'Delete frees');
  L.Objects[0] := TTracked.Create;
  CheckEqI(2, Freed, 'overwrite frees the old object');
  L.Clear;
  CheckEqI(5, Freed, 'Clear frees');
  L.AddObject('e', TTracked.Create);
  L.Free;
  CheckEqI(6, Freed, 'destruction frees');
  L := TStringList.Create;
  L.AddObject('f', TTracked.Create);
  L.Objects[0].Free;
  L.Free;
  CheckEqI(7, Freed, 'not owned: only the explicit Free');

  Section('constructor with options');
  L := TStringList.Create(dupAccept, True, True);
  CheckEqB(True, L.Sorted, 'Sorted');
  CheckEqB(True, L.CaseSensitive, 'CaseSensitive');
  Check(L.Duplicates = dupAccept, 'Duplicates');
  L.Add('b');
  L.Add('B');
  L.Add('b');
  CheckEqS('B b b ', Lines(L), 'behaves accordingly');
  L.Free;
  Done;
end.
