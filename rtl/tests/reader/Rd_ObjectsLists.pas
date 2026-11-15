// phase: 4
// covers: RTL-496
program Rd_ObjectsLists;
{ Property paths, string lists and collections (classes.md RTL-496): a dotted path walks published class properties
  with non-nil values (two levels deep, also inside collection items); "(" ... ")" fills a TStrings inside one
  update after Clear; "<" item ... end ">" clears the collection and adds one item per "item" with its own property
  lines; a path through nil or a non-class property, or an unknown last name, is F0102; a list or collection for a
  property object of another class is F0103. }
uses
  SysUtils, Classes, TestKit;

type
  TInner = class(TPersistent)
  private
    FSize: Integer;
  published
    property Size: Integer read FSize write FSize;
  end;

  TFontX = class(TPersistent)
  private
    FHeight: Integer;
    FName: string;
    FInner: TInner;
  public
    constructor Create;
    destructor Destroy; override;
  published
    property Height: Integer read FHeight write FHeight;
    property Name: string read FName write FName;
    property Inner: TInner read FInner;
  end;

  TColItem = class(TCollectionItem)
  private
    FCaption: string;
    FWidth: Integer;
    FFont: TFontX;
  public
    constructor Create(Collection: TCollection); override;
    destructor Destroy; override;
  published
    property Caption: string read FCaption write FCaption;
    property Width: Integer read FWidth write FWidth;
    property Font: TFontX read FFont;
  end;

  TListComp = class(TComponent)
  private
    FFont: TFontX;
    FLines: TStringList;
    FCols: TOwnedCollection;
    FNilObj: TFontX;
    FCount: Integer;
  public
    Changes: Integer;
    constructor Create(AOwner: TComponent); override;
    destructor Destroy; override;
    procedure LinesChanged(Sender: TObject);
  published
    property Font: TFontX read FFont;
    property Lines: TStringList read FLines;
    property Cols: TOwnedCollection read FCols;
    property NilObj: TFontX read FNilObj;
    property Count: Integer read FCount write FCount;
  end;

constructor TFontX.Create;
begin
  inherited Create;
  FInner := TInner.Create;
end;

destructor TFontX.Destroy;
begin
  FInner.Free;
  inherited Destroy;
end;

constructor TColItem.Create(Collection: TCollection);
begin
  inherited Create(Collection);
  FFont := TFontX.Create;
end;

destructor TColItem.Destroy;
begin
  FFont.Free;
  inherited Destroy;
end;

constructor TListComp.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  FFont := TFontX.Create;
  FLines := TStringList.Create;
  FLines.OnChange := LinesChanged;
  FCols := TOwnedCollection.Create(Self, TColItem);
end;

destructor TListComp.Destroy;
begin
  FCols.Free;
  FLines.Free;
  FFont.Free;
  inherited Destroy;
end;

procedure TListComp.LinesChanged(Sender: TObject);
begin
  Changes := Changes + 1;
end;

function Err(const Lines: string): string;
var
  Q: TListComp;
begin
  Result := '';
  Q := TListComp.Create(nil);
  try
    ReadComponentFromText('object Q: TListComp'#10 + Lines + 'end'#10, Q, 'L.form');
  except
    on E: Exception do
      Result := E.ClassName + ': ' + E.Message;
  end;
  Q.Free;
end;

function Item(L: TListComp; I: Integer): TColItem;
begin
  Result := TColItem(L.Cols.Items[I]);
end;

var
  L: TListComp;
begin
  L := TListComp.Create(nil);
  L.Lines.Add('old');
  TColItem(L.Cols.Add).Caption := 'old';
  L.Changes := 0;

  Section('paths, lists, collections');
  ReadComponentFromText(
    'object L1: TListComp'#10 +
    '  Font.Height = -16'#10 +
    '  Font.Name = ''Sans'''#10 +
    '  Font.Inner.Size = 3'#10 +
    '  Lines = ('#10 +
    '    ''first'''#10 +
    '    ''it''''s'''#10 +
    '    '''''#10 +
    '    ''long '' +'#10 +
    '      ''line'''#10 +
    '  )'#10 +
    '  Cols = <'#10 +
    '    item'#10 +
    '      Caption = ''c1'''#10 +
    '      Width = 10'#10 +
    '      Font.Height = 4'#10 +
    '    end'#10 +
    '    item'#10 +
    '      Caption = ''c2'''#10 +
    '    end'#10 +
    '  >'#10 +
    '  Count = 2'#10 +
    'end'#10, L, 'L.form');
  CheckEqI(-16, L.Font.Height, 'Font.Height');
  CheckEqS('Sans', L.Font.Name, 'Font.Name');
  CheckEqI(3, L.Font.Inner.Size, 'two levels deep');
  CheckEqI(4, L.Lines.Count, 'list cleared and filled');
  CheckEqS('first|it''s||long line', L.Lines[0] + '|' + L.Lines[1] + '|' + L.Lines[2] + '|' + L.Lines[3], 'list items');
  CheckEqI(1, L.Changes, 'one change notification for the whole list');
  CheckEqI(2, L.Cols.Count, 'collection cleared and filled');
  CheckEqS('c1', Item(L, 0).Caption, 'item 1 caption');
  CheckEqI(10, Item(L, 0).Width, 'item 1 width');
  CheckEqI(4, Item(L, 0).Font.Height, 'path inside an item');
  CheckEqS('c2', Item(L, 1).Caption, 'item 2 caption');
  CheckEqI(0, Item(L, 1).Width, 'item 2 width untouched');
  CheckEqI(2, L.Count, 'property after the collection');

  Section('empty list and collection');
  ReadComponentFromText('object L1: TListComp'#10'  Lines = ('#10'  )'#10'  Cols = <'#10'  >'#10'end'#10, L, 'L.form');
  CheckEqI(0, L.Lines.Count, 'empty list');
  CheckEqI(0, L.Cols.Count, 'empty collection');

  Section('errors');
  CheckEqS('EReadError: L.form(2,3): error F0102: Property NilObj.Height does not exist on TListComp',
    Err('  NilObj.Height = 1'#10), 'path through nil');
  CheckEqS('EReadError: L.form(2,3): error F0102: Property Count.X does not exist on TListComp',
    Err('  Count.X = 1'#10), 'path through a non-class property');
  CheckEqS('EReadError: L.form(2,3): error F0102: Property Font.Nope does not exist on TListComp',
    Err('  Font.Nope = 1'#10), 'unknown sub-property');
  CheckEqS('EReadError: L.form(4,7): error F0102: Property Nope does not exist on TColItem',
    Err('  Cols = <'#10'    item'#10'      Nope = 1'#10'    end'#10'  >'#10), 'unknown item property');
  CheckEqS('EReadError: L.form(2,10): error F0103: Invalid value for property Font',
    Err('  Font = ('#10'  )'#10), 'list for a non-TStrings object');
  CheckEqS('EReadError: L.form(2,11): error F0103: Invalid value for property Lines',
    Err('  Lines = <'#10'  >'#10), 'collection for a non-collection object');
  CheckEqS('EReadError: L.form(3,5): error F0103: Invalid value for property Lines',
    Err('  Lines = ('#10'    1'#10'  )'#10), 'number in a string list');
  CheckEqS('EReadError: L.form(2,12): error F0001: Syntax error: expected "item" or ">" but found "x"',
    Err('  Cols = < x'#10'  >'#10), 'junk in a collection');
  L.Free;
  Done;
end.
