// covers: DAT-004, DAT-055, DAT-056, DAT-057, DAT-061, DAT-062
program DsFilter;
{ Filtering. Parse section: the filter grammar and the position and text of every filter error. Evaluation
  section: comparison semantics per field type, null rules, FilterOptions, OnFilterRecord, the view rebuild
  and cursor rule when the filter changes, posting under a filter, bookmarks and Locate in a filtered view. }
uses
  SysUtils, Classes, DB, DataTestUtils, TestDataSet;

const
  OpenSeq = 'BeforeOpen,DriverOpen,DriverInitFieldDefs,DriverLoadRows,[deUpdateState dsBrowse],AfterOpen,AfterScroll';
  ChangeSeq = '[deDataSetChange dsBrowse],AfterScroll';
  PostEditSeq = '[deUpdateRecord dsEdit],BeforePost,ApplyUpdate,[deUpdateState dsBrowse],[deDataSetChange dsBrowse],AfterPost';
  PostInsertSeq = '[deUpdateRecord dsInsert],BeforePost,ApplyInsert,[deUpdateState dsBrowse],[deDataSetChange dsBrowse],AfterPost';

type
  TCalls = class
    DS: TTestDataSet;
    Mark: TBookmark;
    WriteInFilter: Boolean;
    procedure OddIds(DataSet: TDataSet; var Accept: Boolean);
    procedure AcceptUntouched(DataSet: TDataSet; var Accept: Boolean);
    procedure DoGoto;
    procedure DoOpen;
    procedure SetFilteredOn;
  end;

procedure TCalls.OddIds(DataSet: TDataSet; var Accept: Boolean);
begin
  AddLog('filter(' + DataSet.FieldByName('id').AsString + ',' + BoolStr(Accept) + ')');
  if WriteInFilter then
  begin
    WriteInFilter := False;
    try
      DataSet.FieldByName('name').AsString := 'x';
      AddLog('written');
    except
      on E: EDatabaseError do
        AddLog(E.Message);
    end;
  end;
  Accept := Odd(DataSet.FieldByName('id').AsInteger);
end;

procedure TCalls.AcceptUntouched(DataSet: TDataSet; var Accept: Boolean);
begin
end;

procedure TCalls.DoGoto;
begin
  DS.GotoBookmark(Mark);
end;

procedure TCalls.DoOpen;
begin
  DS.Open;
end;

procedure TCalls.SetFilteredOn;
begin
  DS.Filtered := True;
end;

var
  Calls: TCalls;
  Born1, Born2: TDateTime;

function Bytes3: TBytes;
begin
  SetLength(Result, 3);
  Result[0] := 1;
  Result[1] := 2;
  Result[2] := 3;
end;

{ id, name, amount, born, flag, note, pic, 'my col':
  1 Ada  1.5  Born1 True  x     bytes 10
  2 adam 2.25 NULL  False NULL  NULL  20
  3 Bob  NULL Born2 NULL  it's  NULL  30
  4 Ad*  10   Born2 True  NULL  NULL  40
  5 NULL NULL NULL  NULL  NULL  NULL  NULL
  not opened; the row ids equal the ids }
function NewPeople: TTestDataSet;
begin
  Result := TTestDataSet.Create(nil);
  Result.AddDef('id', ftInteger);
  Result.AddDef('name', ftString, 20);
  Result.AddDef('amount', ftFloat);
  Result.AddDef('born', ftDate);
  Result.AddDef('flag', ftBoolean);
  Result.AddDef('note', ftMemo);
  Result.AddDef('pic', ftBlob);
  Result.AddDef('my col', ftInteger);
  Result.AddRow([IntValue(1), StringValue('Ada'), FloatValue(1.5), FloatValue(Born1), IntValue(1), StringValue('x'),
    BytesValue(Bytes3), IntValue(10)]);
  Result.AddRow([IntValue(2), StringValue('adam'), FloatValue(2.25), NullValue, IntValue(0), NullValue, NullValue,
    IntValue(20)]);
  Result.AddRow([IntValue(3), StringValue('Bob'), NullValue, FloatValue(Born2), NullValue, StringValue('it''s'),
    NullValue, IntValue(30)]);
  Result.AddRow([IntValue(4), StringValue('Ad*'), FloatValue(10), FloatValue(Born2), IntValue(1), NullValue, NullValue,
    IntValue(40)]);
  Result.AddRow([NullValue, NullValue, NullValue, NullValue, NullValue, NullValue, NullValue, NullValue]);
  Result.HookEvents;
  Calls.DS := Result;
  Calls.WriteInFilter := False;
end;

function OpenPeople: TTestDataSet;
begin
  Result := NewPeople;
  Result.Open;
  ClearLog;
end;

{ the message of the filter error the expression raises, or 'no error' }
function Err(DS: TDataSet; const Expr: string): string;
begin
  Result := 'no error';
  try
    DS.Filter := Expr;
  except
    on E: EDatabaseError do
      Result := E.Message;
  end;
end;

procedure CheckErr(DS: TDataSet; const Expr: string; Position: Integer; const Msg: string);
begin
  CheckEq(Err(DS, Expr), 'Filter error at position ' + IntToStr(Position) + ': ' + Msg, 'error in <' + Expr + '>');
end;

{ the row ids of the view under the expression }
function Rows(DS: TTestDataSet; const Expr: string): string;
begin
  DS.Filter := Expr;
  Result := DS.ViewRowIds;
end;

procedure CheckRows(DS: TTestDataSet; const Expr, Expected: string);
begin
  CheckEq(Rows(DS, Expr), Expected, '<' + Expr + '>');
end;

procedure TestParseErrors;
var
  DS: TTestDataSet;
begin
  Section('parse: syntax errors');
  DS := OpenPeople;
  DS.Filter := 'id >= 2';
  DS.Filtered := True;
  DS.Last;
  ClearLog;
  CheckErr(DS, 'id', 3, 'Comparison operator expected');
  CheckErr(DS, 'id = ', 6, 'Operand expected');
  CheckErr(DS, '= 1', 1, 'Field name expected');
  CheckErr(DS, 'id == 1', 5, 'Operand expected');
  CheckErr(DS, 'id = 1 AND', 11, 'Field name expected');
  CheckErr(DS, 'id = 1 AND OR id = 2', 12, 'Field name expected');
  CheckErr(DS, 'NOT', 4, 'Field name expected');
  CheckErr(DS, 'NOT NOT id = 1', 5, 'Field name expected');
  CheckErr(DS, 'null = 1', 1, 'Field name expected');
  CheckErr(DS, '5 = id', 1, 'Field name expected');
  CheckErr(DS, '(id = 1', 8, 'Missing '')''');
  CheckErr(DS, '(id = 1 id = 2)', 9, 'Missing '')''');
  CheckErr(DS, 'id = 1)', 7, 'End of filter expected');
  CheckErr(DS, 'id = 1 name = 2', 8, 'End of filter expected');
  CheckErr(DS, 'id IS 1', 7, 'NULL expected');
  CheckErr(DS, 'id IS NOT', 10, 'NULL expected');
  CheckErr(DS, 'id < > 1', 6, 'Operand expected');
  CheckErr(DS, 'id = AND', 6, 'Operand expected');

  Section('parse: lexical errors');
  CheckErr(DS, 'name = ''abc', 8, 'Unterminated string');
  CheckErr(DS, 'name = ''it''''s', 8, 'Unterminated string');
  CheckErr(DS, '[id = 1', 1, 'Missing '']''');
  CheckErr(DS, 'id = 1 AND [name', 12, 'Missing '']''');
  CheckErr(DS, 'id # 1', 4, 'Unexpected character ''#''');
  CheckErr(DS, 'id = -', 6, 'Unexpected character ''-''');
  CheckErr(DS, 'id = 1.', 7, 'Unexpected character ''.''');
  CheckErr(DS, 'id = 1 ; id = 2', 8, 'Unexpected character '';''');

  Section('parse: fields and operand types');
  CheckErr(DS, 'nope = 1', 1, 'Field ''nope'' not found');
  CheckErr(DS, 'id = 1 OR [no such] = 2', 11, 'Field ''no such'' not found');
  CheckErr(DS, 'id = nope', 6, 'Field ''nope'' not found');
  CheckErr(DS, 'id = ''abc''', 6, '''abc'' is not a valid value for field ''id''');
  CheckErr(DS, 'amount > ''1x''', 10, '''1x'' is not a valid value for field ''amount''');
  CheckErr(DS, 'born = ''not a date''', 8, '''not a date'' is not a valid value for field ''born''');
  CheckErr(DS, 'flag = ''maybe''', 8, '''maybe'' is not a valid value for field ''flag''');
  CheckErr(DS, 'name = 5', 8, 'Operand does not match the type of field ''name''');
  CheckErr(DS, 'name = TRUE', 8, 'Operand does not match the type of field ''name''');
  CheckErr(DS, 'id = TRUE', 6, 'Operand does not match the type of field ''id''');
  CheckErr(DS, 'flag = 1', 8, 'Operand does not match the type of field ''flag''');
  CheckErr(DS, 'born = 5', 8, 'Operand does not match the type of field ''born''');
  CheckErr(DS, 'pic = ''x''', 1, 'Field ''pic'' cannot be compared');
  CheckErr(DS, 'id = pic', 6, 'Field ''pic'' cannot be compared');

  Section('parse: an error changes nothing');
  CheckEq(DS.Filter, 'id >= 2', 'the old filter is kept');
  CheckEq(DS.ViewRowIds, '2,3,4', 'the view is unchanged');
  CheckEq(TakeLog, '', 'no events');
  CheckEq(StateName(DS.State) + ' ' + IntToStr(DS.RecNo) + ' ' + Flags(DS), 'dsBrowse 3 Eof', 'state and cursor are unchanged');
  DS.Edit;
  DS.FieldByName('name').AsString := 'changed';
  ClearLog;
  CheckErr(DS, 'id =', 5, 'Operand expected');
  CheckEq(TakeLog, '', 'an error is raised before the pending edit is posted');
  CheckEq(StateName(DS.State), 'dsEdit', 'the dataset stays in dsEdit');
  DS.Cancel;

  Section('parse: accepted forms');
  CheckEq(Err(DS, ''), 'no error', 'the empty filter');
  CheckEq(Err(DS, '   '), 'no error', 'blanks only');
  CheckEq(Err(DS, 'ID=1'), 'no error', 'no blanks');
  CheckEq(Err(DS, '((id = 1))'), 'no error', 'nested parentheses');
  CheckEq(Err(DS, 'id is null or Id Is Not Null'), 'no error', 'keywords in any case');
  CheckEq(Err(DS, 'id = NULL and name <> null'), 'no error', 'NULL as an operand');
  CheckEq(Err(DS, 'amount >= -12.5'), 'no error', 'a negative number with decimals');
  CheckEq(Err(DS, '[my col] = 1 and [id] = [my col]'), 'no error', 'bracketed field names');
  CheckEq(Err(DS, 'name = ''a''''b'' and note = '''''), 'no error', 'doubled quotes and the empty string');
  CheckEq(Err(DS, 'pic IS NULL or pic = NULL or pic <> NULL'), 'no error', 'a blob field can be tested for null');
  CheckEq(Err(DS, 'id = ''12'' and amount = ''' + FloatToStr(1.5) + ''''), 'no error', 'numbers as string literals');
  DS.Free;

  Section('parse: closed dataset');
  DS := NewPeople;
  CheckErr(DS, 'id = ', 6, 'Operand expected');
  CheckEq(DS.Filter, '', 'a syntax error is raised at once and nothing is stored');
  CheckEq(Err(DS, 'nope = 1'), 'no error', 'field names are checked when the dataset opens');
  DS.Filtered := True;
  ClearLog;
  CheckRaises(Calls.DoOpen, 'EDatabaseError', 'Filter error at position 1: Field ''nope'' not found', 'Open with a bad filter');
  CheckEq(TakeLog, 'BeforeOpen,DriverOpen,DriverInitFieldDefs,DriverLoadRows,DriverClose', 'the open is undone');
  CheckEq(StateName(DS.State), 'dsInactive', 'the dataset stays closed');
  DS.Filtered := False;
  DS.Open;
  CheckEqInt(DS.RecordCount, 5, 'without Filtered the bad filter does not matter');
  CheckRaises(Calls.SetFilteredOn, 'EDatabaseError', 'Filter error at position 1: Field ''nope'' not found', 'Filtered := True with a bad filter');
  CheckEqBool(DS.Filtered, False, 'Filtered stays False');
  DS.Free;
end;

procedure TestCompare;
var
  DS: TTestDataSet;
begin
  Section('compare: numbers');
  DS := OpenPeople;
  DS.Filtered := True;
  CheckRows(DS, '', '1,2,3,4,5');
  CheckRows(DS, 'id = 2', '2');
  CheckRows(DS, 'id <> 2', '1,3,4');
  CheckRows(DS, 'id < 3', '1,2');
  CheckRows(DS, 'id <= 3', '1,2,3');
  CheckRows(DS, 'id > 3', '4');
  CheckRows(DS, 'id >= 3', '3,4');
  CheckRows(DS, '  ID=2  ', '2');
  CheckRows(DS, 'id = ''3''', '3');
  CheckRows(DS, 'amount > 2', '2,4');
  CheckRows(DS, 'amount = 2.25', '2');
  CheckRows(DS, 'amount = 1.50', '1');
  CheckRows(DS, 'amount > -1', '1,2,4');
  CheckRows(DS, 'amount < 2.5', '1,2');
  CheckRows(DS, '[my col] >= 30', '3,4');

  Section('compare: strings and options');
  CheckRows(DS, 'name = ''Ada''', '1');
  CheckRows(DS, 'name = ''ada''', '');
  CheckRows(DS, 'name < ''B''', '1,4');
  CheckRows(DS, 'name >= ''Bob''', '2,3');
  CheckRows(DS, 'name = ''Ad*''', '1,4');
  CheckRows(DS, 'name <> ''Ad*''', '2,3');
  CheckRows(DS, 'name = ''*''', '1,2,3,4');
  CheckRows(DS, 'name > ''Ad*''', '1,2,3');
  CheckRows(DS, 'note = ''it''''s''', '3');
  CheckRows(DS, 'note = ''x''', '1');
  DS.FilterOptions := [foCaseInsensitive];
  CheckEq(DS.ViewRowIds, '1', 'changing FilterOptions rebuilds the view');
  CheckRows(DS, 'name = ''ada''', '1');
  CheckRows(DS, 'name < ''B''', '1,2,4');
  CheckRows(DS, 'name = ''AD*''', '1,2,4');
  CheckRows(DS, 'name <> ''aD*''', '3');
  DS.FilterOptions := [foNoPartialCompare];
  CheckRows(DS, 'name = ''Ad*''', '4');
  CheckRows(DS, 'name <> ''Ad*''', '1,2,3');
  CheckRows(DS, 'name = ''AD*''', '');
  DS.FilterOptions := [foCaseInsensitive, foNoPartialCompare];
  CheckRows(DS, 'name = ''AD*''', '4');
  DS.FilterOptions := [];

  Section('compare: null');
  CheckRows(DS, 'amount IS NULL', '3,5');
  CheckRows(DS, 'amount IS NOT NULL', '1,2,4');
  CheckRows(DS, 'amount = NULL', '3,5');
  CheckRows(DS, 'amount <> NULL', '1,2,4');
  CheckRows(DS, 'amount < NULL', '');
  CheckRows(DS, 'amount >= NULL', '');
  CheckRows(DS, 'pic IS NULL', '2,3,4,5');
  CheckRows(DS, 'pic is not null', '1');
  CheckRows(DS, 'name = ''''', '');
  CheckRows(DS, 'NOT amount > 2', '1,3,5');

  Section('compare: boolean and date');
  CheckRows(DS, 'flag = TRUE', '1,4');
  CheckRows(DS, 'flag = false', '2');
  CheckRows(DS, 'flag <> TRUE', '2');
  CheckRows(DS, 'flag = ''true''', '1,4');
  CheckRows(DS, 'flag = ''0''', '2');
  CheckEq(Rows(DS, 'born = ''' + DateToStr(Born2) + ''''), '3,4', 'a date equal to a string literal');
  CheckEq(Rows(DS, 'born > ''' + DateToStr(Born2) + ''''), '1', 'a date after a string literal');
  CheckEq(Rows(DS, 'born <= ''' + DateToStr(Born1) + ''''), '1,3,4', 'a date up to a string literal');

  Section('compare: field with field');
  CheckRows(DS, 'id < amount', '1,2,4');
  CheckRows(DS, 'id > amount', '');
  CheckRows(DS, '[my col] > id', '1,2,3,4');
  CheckRows(DS, 'name <> note', '1,3');
  CheckRows(DS, 'name = name', '1,2,3,4');
  CheckRows(DS, 'id = flag', '1');
  CheckRows(DS, 'id = name', '');

  Section('logic');
  CheckRows(DS, 'id = 1 OR id = 3', '1,3');
  CheckRows(DS, 'id > 1 AND id < 4', '2,3');
  CheckRows(DS, 'id = 1 OR id = 2 AND amount > 5', '1');
  CheckRows(DS, '(id = 1 OR id = 2) AND amount > 2', '2');
  CheckRows(DS, 'NOT id = 2', '1,3,4,5');
  CheckRows(DS, 'NOT (id = 1 OR id = 2)', '3,4,5');
  CheckRows(DS, 'not id = 1 and not id = 2', '3,4,5');
  CheckRows(DS, 'id = 1 or ID = 2 Or [Id] = 4', '1,2,4');
  CheckRows(DS, '((id = 3))', '3');
  CheckEq(DS.StoredRowIds, '1,2,3,4,5', 'the row list is never filtered');
  DS.Free;
end;

procedure TestViewRebuild;
var
  DS: TTestDataSet;
  I: Integer;
  Seen: string;
begin
  Section('view rebuild: cursor and events');
  DS := OpenPeople;
  DS.RecNo := 3;
  ClearLog;
  DS.Filter := 'id >= 2';
  CheckEq(TakeLog, '', 'Filter without Filtered: no events');
  CheckEqInt(DS.RecordCount, 5, 'and no effect');
  DS.Filtered := True;
  CheckEq(TakeLog, ChangeSeq, 'Filtered := True: event order');
  CheckEq(DS.ViewRowIds, '2,3,4', 'the view');
  CheckEqInt(DS.RecNo, 2, 'the cursor stays on its row');
  CheckEq(DS.FieldByName('name').AsString, 'Bob', 'the same row');
  CheckEq(Flags(DS), '-', 'flags unchanged');
  DS.Filtered := True;
  CheckEq(TakeLog, '', 'the same value again: nothing happens');
  DS.Filter := 'id < 3';
  CheckEq(TakeLog, ChangeSeq, 'Filter while Filtered: event order');
  CheckEq(DS.ViewRowIds, '1,2', 'the new view');
  CheckEqInt(DS.RecNo, 1, 'the row is no longer visible: first row');
  CheckEq(Flags(DS), 'Bof', 'with Bof');
  DS.Next;
  ClearLog;
  DS.FilterOptions := [foCaseInsensitive];
  CheckEq(TakeLog, ChangeSeq, 'FilterOptions while Filtered: event order');
  CheckEqInt(DS.RecNo, 2, 'the cursor stays');
  DS.Filtered := False;
  CheckEq(TakeLog, ChangeSeq, 'Filtered := False: event order');
  CheckEq(DS.ViewRowIds, '1,2,3,4,5', 'all rows again');
  CheckEqInt(DS.RecNo, 2, 'still on the row');
  DS.FilterOptions := [];
  CheckEq(TakeLog, '', 'FilterOptions without Filtered: no events');
  DS.Filter := 'id > 100';
  DS.Filtered := True;
  CheckEq(TakeLog, ChangeSeq, 'a filter no row passes');
  CheckEq(IntToStr(DS.RecordCount) + ' ' + IntToStr(DS.RecNo) + ' ' + Flags(DS) + ' ' + BoolStr(DS.IsEmpty),
    '0 0 Bof+Eof True', 'the view is empty');
  CheckEqBool(DS.FieldByName('id').IsNull, True, 'fields read null');
  DS.Filter := '';
  CheckEq(DS.ViewRowIds, '1,2,3,4,5', 'the empty filter shows all rows');
  CheckEq(IntToStr(DS.RecNo) + ' ' + Flags(DS), '1 Bof', 'from an empty view: first row');
  { a row that stays the current row but is no longer the first or the last row of the view loses the flag }
  DS.Filter := 'id >= 3';
  CheckEq(IntToStr(DS.RecNo) + ' ' + Flags(DS), '1 Bof', 'the first row of a narrower view');
  DS.Filtered := False;
  CheckEq(IntToStr(DS.RecNo) + ' ' + Flags(DS), '3 -', 'kept, no longer the first row: Bof is cleared');
  DS.Filter := 'id <= 3';
  DS.Filtered := True;
  DS.Next;
  CheckEq(IntToStr(DS.RecNo) + ' ' + Flags(DS), '3 Eof', 'at the end of a narrower view');
  DS.Filtered := False;
  CheckEq(IntToStr(DS.RecNo) + ' ' + Flags(DS), '3 -', 'kept, no longer the last row: Eof is cleared');
  DS.First;
  DS.Filter := 'id <= 3';
  DS.Filtered := True;
  CheckEq(IntToStr(DS.RecNo) + ' ' + Flags(DS), '1 Bof', 'kept and still the first row: Bof stays');
  DS.Filter := '';

  Section('view rebuild: browse mode first');
  DS.RecNo := 2;
  DS.Edit;
  DS.FieldByName('amount').AsFloat := 20;
  ClearLog;
  DS.Filter := 'amount > 5';
  CheckEq(TakeLog, '[deCheckBrowseMode dsEdit],' + PostEditSeq + ',' + ChangeSeq, 'a modified record is posted first');
  CheckEq(DS.ViewRowIds, '2,4', 'the posted value is filtered');
  CheckEqInt(DS.RecNo, 1, 'the cursor stays on the posted row');

  Section('navigation in a filtered view');
  Seen := '';
  DS.First;
  while not DS.Eof do
  begin
    Seen := Seen + DS.FieldByName('id').AsString + ' ';
    DS.Next;
  end;
  CheckEq(Seen, '2 4 ', 'the loop visits the visible rows');
  DS.Last;
  CheckEqInt(DS.RecNo, 2, 'RecNo counts view rows');
  I := DS.MoveBy(-5);
  CheckEqInt(I, -1, 'MoveBy is limited by the view');
  CheckEqBool(DS.Locate('id', ['1'], []), False, 'Locate does not see hidden rows');
  CheckEqBool(DS.Locate('id', ['4'], []), True, 'Locate finds a visible row');

  Section('close and open');
  DS.Close;
  ClearLog;
  DS.Open;
  CheckEq(TakeLog, OpenSeq, 'Open with a filter: the usual events');
  CheckEq(DS.ViewRowIds, '4', 'the filter is applied at open (the rows are loaded again)');
  CheckEq(DS.Filter, 'amount > 5', 'Filter is kept across Close');
  DS.Close;
  DS.Filter := 'id < 3';
  DS.FilterOptions := [foNoPartialCompare];
  DS.Filtered := False;
  CheckEq(TakeLog, 'BeforeClose,DriverClose,[deUpdateState dsInactive],AfterClose', 'the filter properties of a closed dataset fire no events');
  DS.Open;
  CheckEqInt(DS.RecordCount, 5, 'opened unfiltered');
  DS.Filtered := True;
  CheckEq(DS.ViewRowIds, '1,2', 'the filter assigned while closed');
  DS.Refresh;
  CheckEq(DS.ViewRowIds, '6,7', 'Refresh filters the reloaded rows');
  DS.Free;
end;

procedure TestPost;
var
  DS: TTestDataSet;
begin
  Section('post under a filter');
  DS := OpenPeople;
  DS.Filter := 'amount > 2';
  DS.Filtered := True;
  CheckEq(DS.ViewRowIds, '2,4', 'the view');
  DS.Edit;
  DS.FieldByName('amount').AsFloat := 3;
  DS.Post;
  CheckEq(DS.ViewRowIds, '2,4', 'a row that still passes stays');
  CheckEqInt(DS.RecNo, 1, 'and stays current');
  DS.Edit;
  DS.FieldByName('amount').AsFloat := 1;
  ClearLog;
  DS.Post;
  CheckEq(TakeLog, PostEditSeq, 'Post: the usual events, no scroll events');
  CheckEq(DS.ViewRowIds, '4', 'a row that no longer passes leaves the view');
  CheckEq(DS.StoredRowIds, '1,2,3,4,5', 'but not the row list');
  CheckEq(DS.FieldByName('id').AsString, '4', 'the cursor is on the following row');
  DS.Insert;
  DS.FieldByName('id').AsInteger := 6;
  DS.FieldByName('amount').AsFloat := 50;
  DS.Post;
  CheckEq(DS.ViewRowIds, '6,4', 'an inserted row that passes is shown at its position');
  CheckEqInt(DS.RecNo, 1, 'and is current');
  DS.Append;
  DS.FieldByName('id').AsInteger := 7;
  DS.FieldByName('amount').AsFloat := 0;
  ClearLog;
  DS.Post;
  CheckEq(TakeLog, PostInsertSeq, 'Post of a hidden insert: the usual events');
  CheckEq(DS.ViewRowIds, '6,4', 'an inserted row that does not pass is hidden');
  CheckEq(DS.StoredRowIds, '1,2,3,6,4,5,7', 'and stored');
  CheckEq(DS.FieldByName('id').AsString, '4', 'the cursor is on the last row');
  DS.Edit;
  DS.FieldByName('amount').Clear;
  DS.Post;
  CheckEq(DS.FieldByName('id').AsString, '6', 'the last row leaves the view: the new last row is current');
  DS.Edit;
  DS.FieldByName('amount').AsFloat := 0;
  DS.Post;
  CheckEq(IntToStr(DS.RecordCount) + ' ' + IntToStr(DS.RecNo) + ' ' + Flags(DS), '0 0 Bof+Eof', 'the only row leaves the view: empty');
  DS.Filtered := False;
  CheckEqInt(DS.RecordCount, 7, 'all rows are still there');
  DS.Filter := 'id >= 6';
  DS.Filtered := True;
  DS.First;
  DS.Delete;
  CheckEq(DS.ViewRowIds, '7', 'Delete removes the row from the view');
  CheckEq(DS.StoredRowIds, '1,2,3,4,5,7', 'and from the row list');
  DS.Free;
end;

procedure TestFilterRecord;
var
  DS: TTestDataSet;
begin
  Section('OnFilterRecord');
  DS := OpenPeople;
  DS.OnFilterRecord := Calls.OddIds;
  CheckEq(TakeLog, '', 'assigning the handler does nothing by itself');
  DS.Filtered := True;
  CheckEq(TakeLog, 'filter(1,True),filter(2,True),filter(3,True),filter(4,True),filter(,True),' + ChangeSeq,
    'the handler sees every row; Accept starts as True');
  CheckEq(DS.ViewRowIds, '1,3', 'the rows it accepts');
  DS.Filter := 'id > 1';
  CheckEq(TakeLog, 'filter(2,True),filter(3,True),filter(4,True),' + ChangeSeq,
    'the handler is asked only for rows the expression passes');
  CheckEq(DS.ViewRowIds, '3', 'expression and handler must both accept');
  CheckEqInt(DS.RecNo, 1, 'the cursor is on the visible row');
  Calls.WriteInFilter := True;
  DS.Filter := '';
  CheckEq(TakeLog, 'filter(1,True),Dataset not in edit or insert mode,filter(2,True),filter(3,True),filter(4,True),' +
    'filter(,True),' + ChangeSeq, 'a field write in the handler is refused');
  CheckEq(DS.StoredText, '[1|Ada|1.5|' + FloatToStr(Born1) + '|1|x|bytes(3)|10] [2|adam|2.25|NULL|0|NULL|NULL|20] ' +
    '[3|Bob|NULL|' + FloatToStr(Born2) + '|NULL|it''s|NULL|30] [4|Ad*|10|' + FloatToStr(Born2) + '|1|NULL|NULL|40] ' +
    '[NULL|NULL|NULL|NULL|NULL|NULL|NULL|NULL]', 'the rows are untouched');
  CheckEq(DS.FieldByName('id').AsString, '3', 'after the rebuild the fields read the current row again');
  DS.Edit;
  DS.FieldByName('id').AsInteger := 8;
  ClearLog;
  DS.Post;
  CheckEq(TakeLog, '[deUpdateRecord dsEdit],BeforePost,ApplyUpdate,filter(8,True),[deUpdateState dsBrowse],[deDataSetChange dsBrowse],AfterPost',
    'Post asks the handler for the posted row');
  CheckEq(DS.ViewRowIds, '1', 'and removes the row it rejects');
  DS.OnFilterRecord := Calls.AcceptUntouched;
  DS.Filtered := False;
  DS.Filtered := True;
  CheckEq(DS.ViewRowIds, '1,2,3,4,5', 'a handler that leaves Accept alone accepts every row');
  DS.Free;
end;

procedure TestBookmarks;
var
  DS: TTestDataSet;
  B1, B2, B3: TBookmark;
begin
  Section('bookmarks in a filtered view');
  DS := OpenPeople;
  B1 := DS.GetBookmark;
  DS.Next;
  B2 := DS.GetBookmark;
  DS.Next;
  B3 := DS.GetBookmark;
  DS.Filter := 'id >= 3';
  DS.Filtered := True;
  CheckEqBool(DS.BookmarkValid(B3), True, 'a visible row');
  CheckEqBool(DS.BookmarkValid(B1), False, 'a hidden row');
  Calls.Mark := B1;
  CheckRaises(Calls.DoGoto, 'EDatabaseError', 'Bookmark is not valid', 'GotoBookmark to a hidden row');
  CheckEqInt(DS.CompareBookmarks(B2, B1), 1, 'hidden rows compare by row id');
  CheckEqInt(DS.CompareBookmarks(B1, B3), -1, 'a hidden and a visible row compare by row id');
  DS.Filtered := False;
  CheckEqBool(DS.BookmarkValid(B1), True, 'valid again without the filter');
  DS.GotoBookmark(B1);
  CheckEqInt(DS.RecNo, 1, 'and reachable');
  DS.Free;
end;

begin
  Calls := TCalls.Create;
  Born1 := EncodeDate(2000, 1, 2);
  Born2 := EncodeDate(1999, 12, 31);
  TestParseErrors;
  TestCompare;
  TestViewRebuild;
  TestPost;
  TestFilterRecord;
  TestBookmarks;
  Calls.Free;
  WriteLn('failed checks: ', FailedChecks);
end.
