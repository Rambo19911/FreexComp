// covers: DAT-060, DAT-061, DAT-062, DAT-063
program DsBookmarkLocate;
{ Bookmarks (the row id as 8 little-endian bytes, validity, GotoBookmark, CompareBookmarks) and Locate (key
  fields and values, options, typed compare, null keys, events). }
uses
  SysUtils, Classes, DB, DataTestUtils, TestDataSet;

const
  ScrollSeq = 'BeforeScroll,[deDataSetScroll dsBrowse],AfterScroll';
  PostEditSeq = '[deUpdateRecord dsEdit],BeforePost,ApplyUpdate,[deUpdateState dsBrowse],[deDataSetChange dsBrowse],AfterPost';
  SClosed = 'Cannot perform this operation on a closed dataset';
  SBad = 'Bookmark is not valid';

type
  TCalls = class
    DS: TTestDataSet;
    Mark: TBookmark;
    procedure DoGoto;
    procedure DoGetBookmark;
    procedure DoBookmarkValid;
    procedure DoCompare;
    procedure LocateTwoFieldsOneValue;
    procedure LocateOneFieldTwoValues;
    procedure LocateMissingField;
    procedure LocateName;
  end;

procedure TCalls.DoGoto;
begin
  DS.GotoBookmark(Mark);
end;

procedure TCalls.DoGetBookmark;
begin
  DS.GetBookmark;
end;

procedure TCalls.DoBookmarkValid;
begin
  DS.BookmarkValid(Mark);
end;

procedure TCalls.DoCompare;
begin
  DS.CompareBookmarks(Mark, Mark);
end;

procedure TCalls.LocateTwoFieldsOneValue;
begin
  DS.Locate('id;name', ['1'], []);
end;

procedure TCalls.LocateOneFieldTwoValues;
begin
  DS.Locate('id', ['1', 'Ada'], []);
end;

procedure TCalls.LocateMissingField;
begin
  DS.Locate('id;nope', ['1', 'x'], []);
end;

procedure TCalls.LocateName;
begin
  DS.Locate('name', ['Ada'], []);
end;

var
  Calls: TCalls;
  Born1, Born2: TDateTime;

function Hex(const B: TBookmark): string;
var
  I: Integer;
begin
  if B = nil then
    Result := 'nil'
  else
  begin
    Result := '';
    for I := 0 to High(B) do
      Result := Result + IntToHex(B[I], 2);
  end;
end;

function Bytes3: TBytes;
begin
  SetLength(Result, 3);
  Result[0] := 1;
  Result[1] := 2;
  Result[2] := 3;
end;

{ id, name; RowCount rows 1 'one', 2 'two', 3 'three', 4 'four'; opened, all events hooked }
function NewDS(RowCount: Integer): TTestDataSet;
const
  Names: array[1..4] of string = ('one', 'two', 'three', 'four');
var
  I: Integer;
begin
  Result := TTestDataSet.Create(nil);
  Result.AddDef('id', ftInteger);
  Result.AddDef('name', ftString, 10);
  for I := 1 to RowCount do
    Result.AddRow([IntValue(I), StringValue(Names[I])]);
  Result.HookEvents;
  Result.Open;
  ClearLog;
  Calls.DS := Result;
end;

{ id, name, amount, born, flag, big, note, pic:
  1 Ada 1.5 Born1 True 5000000000 'first note' bytes(3)
  2 adam 2.25 NULL False NULL NULL NULL
  3 Bob NULL Born2 NULL 7 'Second' NULL
  4 Ada 10 Born2 True NULL NULL NULL
  5 NULL NULL NULL NULL NULL NULL NULL }
function NewPeople: TTestDataSet;
begin
  Result := TTestDataSet.Create(nil);
  Result.AddDef('id', ftInteger);
  Result.AddDef('name', ftString, 20);
  Result.AddDef('amount', ftFloat);
  Result.AddDef('born', ftDate);
  Result.AddDef('flag', ftBoolean);
  Result.AddDef('big', ftLargeint);
  Result.AddDef('note', ftMemo);
  Result.AddDef('pic', ftBlob);
  Result.AddRow([IntValue(1), StringValue('Ada'), FloatValue(1.5), FloatValue(Born1), IntValue(1),
    IntValue(5000000000), StringValue('first note'), BytesValue(Bytes3)]);
  Result.AddRow([IntValue(2), StringValue('adam'), FloatValue(2.25), NullValue, IntValue(0), NullValue,
    NullValue, NullValue]);
  Result.AddRow([IntValue(3), StringValue('Bob'), NullValue, FloatValue(Born2), NullValue, IntValue(7),
    StringValue('Second'), NullValue]);
  Result.AddRow([IntValue(4), StringValue('Ada'), FloatValue(10), FloatValue(Born2), IntValue(1), NullValue,
    NullValue, NullValue]);
  Result.AddRow([NullValue, NullValue, NullValue, NullValue, NullValue, NullValue, NullValue, NullValue]);
  Result.HookEvents;
  Result.Open;
  ClearLog;
  Calls.DS := Result;
end;

procedure TestBookmarkValue;
var
  DS: TTestDataSet;
  B, B3: TBookmark;
begin
  Section('bookmarks: value');
  DS := NewDS(3);
  B := DS.GetBookmark;
  CheckEq(Hex(B), '0100000000000000', 'the row id as 8 little-endian bytes');
  DS.Last;
  B3 := DS.GetBookmark;
  CheckEq(Hex(B3), '0300000000000000', 'the bookmark of the third row');
  CheckEq(Hex(DS.Bookmark), '0300000000000000', 'the Bookmark property reads GetBookmark');
  DS.Edit;
  CheckEq(Hex(DS.GetBookmark), '0300000000000000', 'in dsEdit: the row being edited');
  DS.Cancel;
  DS.Insert;
  CheckEq(Hex(DS.GetBookmark), 'nil', 'in dsInsert: nil');
  DS.Cancel;
  DS.FreeBookmark(B3);
  CheckEq(Hex(B3), 'nil', 'FreeBookmark sets nil');
  DS.Free;

  DS := NewDS(0);
  CheckEq(Hex(DS.GetBookmark), 'nil', 'empty dataset: nil');
  DS.Close;
  Calls.Mark := B;
  CheckRaises(Calls.DoGetBookmark, 'EDatabaseError', SClosed, 'GetBookmark on a closed dataset');
  CheckRaises(Calls.DoGoto, 'EDatabaseError', SClosed, 'GotoBookmark on a closed dataset');
  CheckRaises(Calls.DoBookmarkValid, 'EDatabaseError', SClosed, 'BookmarkValid on a closed dataset');
  CheckRaises(Calls.DoCompare, 'EDatabaseError', SClosed, 'CompareBookmarks on a closed dataset');
  DS.Free;
end;

procedure TestBookmarkValid;
var
  DS: TTestDataSet;
  B1, B2, Unknown: TBookmark;
begin
  Section('bookmarks: validity');
  DS := NewDS(3);
  B1 := DS.GetBookmark;
  DS.Next;
  B2 := DS.GetBookmark;
  CheckEqBool(DS.BookmarkValid(B1), True, 'a bookmark of a row of the view');
  CheckEqBool(DS.BookmarkValid(nil), False, 'nil');
  CheckEqBool(DS.BookmarkValid(Bytes3), False, 'wrong length');
  Unknown := Copy(B1, 0, 8);
  Unknown[0] := 99;
  CheckEqBool(DS.BookmarkValid(Unknown), False, 'unknown row id');
  DS.Delete;
  CheckEqBool(DS.BookmarkValid(B2), False, 'the row was deleted');
  CheckEqBool(DS.BookmarkValid(B1), True, 'another row stays valid');
  ClearLog;
  Calls.Mark := B2;
  CheckRaises(Calls.DoGoto, 'EDatabaseError', SBad, 'GotoBookmark: deleted row');
  Calls.Mark := nil;
  CheckRaises(Calls.DoGoto, 'EDatabaseError', SBad, 'GotoBookmark: nil');
  Calls.Mark := Bytes3;
  CheckRaises(Calls.DoGoto, 'EDatabaseError', SBad, 'GotoBookmark: wrong length');
  CheckEq(TakeLog, '', 'an invalid bookmark fires no events');
  CheckEqInt(DS.RecNo, 2, 'and leaves the cursor');
  DS.Refresh;
  CheckEqBool(DS.BookmarkValid(B1), False, 'bookmarks are invalid after Refresh');
  CheckEq(DS.ViewRowIds, '4,5,6', 'the reloaded rows have new ids');
  DS.Free;
end;

procedure TestGotoBookmark;
var
  DS: TTestDataSet;
  B1, B3: TBookmark;
begin
  Section('bookmarks: GotoBookmark');
  DS := NewDS(3);
  B1 := DS.GetBookmark;
  DS.Last;
  B3 := DS.GetBookmark;
  CheckEq(Flags(DS), 'Eof', 'at the end');
  ClearLog;
  DS.GotoBookmark(B1);
  CheckEq(TakeLog, ScrollSeq, 'event order');
  CheckEqInt(DS.RecNo, 1, 'the cursor is on the row');
  CheckEq(Flags(DS), '-', 'Bof and Eof are False');
  DS.GotoBookmark(B1);
  CheckEq(TakeLog, ScrollSeq, 'the current row: the events still fire');
  DS.Bookmark := B3;
  CheckEq(TakeLog, ScrollSeq, 'Bookmark := is GotoBookmark');
  CheckEqInt(DS.RecNo, 3, 'the cursor follows');

  DS.Edit;
  DS.FieldByName('name').AsString := 'drei';
  ClearLog;
  DS.GotoBookmark(B1);
  CheckEq(TakeLog, '[deCheckBrowseMode dsEdit],' + PostEditSeq + ',' + ScrollSeq, 'a modified record is posted first');
  CheckEq(DS.ViewText, '[1|one] [2|two] [3|drei]', 'the edit is stored');
  CheckEqInt(DS.RecNo, 1, 'then the cursor moves');
  DS.Free;
end;

procedure TestCompareBookmarks;
var
  DS: TTestDataSet;
  B1, B2, B3, BNew, Gone1, Gone2: TBookmark;
begin
  Section('bookmarks: CompareBookmarks');
  DS := NewDS(4);
  B1 := DS.GetBookmark;
  DS.Next;
  B2 := DS.GetBookmark;
  DS.Next;
  B3 := DS.GetBookmark;
  CheckEqInt(DS.CompareBookmarks(nil, nil), 0, 'nil = nil');
  CheckEqInt(DS.CompareBookmarks(nil, B1), -1, 'nil < a bookmark');
  CheckEqInt(DS.CompareBookmarks(B1, nil), 1, 'a bookmark > nil');
  CheckEqInt(DS.CompareBookmarks(B1, B3), -1, 'an earlier row');
  CheckEqInt(DS.CompareBookmarks(B3, B2), 1, 'a later row');
  CheckEqInt(DS.CompareBookmarks(B2, Copy(B2, 0, 8)), 0, 'the same row');
  { a new row (id 5) inserted before the first row }
  DS.First;
  DS.Insert;
  DS.FieldByName('id').AsInteger := 0;
  DS.Post;
  BNew := DS.GetBookmark;
  CheckEq(Hex(BNew), '0500000000000000', 'the new row has the next id');
  CheckEq(DS.ViewRowIds, '5,1,2,3,4', 'and is first in the view');
  CheckEqInt(DS.CompareBookmarks(BNew, B1), -1, 'by position in the view, not by row id');
  CheckEqInt(DS.CompareBookmarks(B3, BNew), 1, 'in both directions');
  { rows that are not in the view compare by row id }
  DS.RecNo := 2;
  Gone1 := DS.GetBookmark;
  DS.Delete;
  Gone2 := DS.GetBookmark;
  DS.Delete;
  CheckEq(DS.ViewRowIds, '5,3,4', 'rows 1 and 2 are deleted');
  CheckEqInt(DS.CompareBookmarks(Gone1, Gone2), -1, 'deleted rows: by row id');
  CheckEqInt(DS.CompareBookmarks(Gone2, Gone1), 1, 'deleted rows: by row id, reversed');
  CheckEqInt(DS.CompareBookmarks(Gone2, Copy(Gone2, 0, 8)), 0, 'the same deleted row');
  CheckEqInt(DS.CompareBookmarks(Gone2, BNew), -1, 'a deleted row and a row of the view: by row id');
  DS.Free;
end;

procedure TestLocateString;
var
  DS: TTestDataSet;
begin
  Section('locate: string keys and options');
  DS := NewPeople;
  DS.Last;
  ClearLog;
  CheckEqBool(DS.Locate('name', ['Ada'], []), True, 'found');
  CheckEq(TakeLog, ScrollSeq, 'found: event order');
  CheckEqInt(DS.RecNo, 1, 'the scan starts at the first row');
  CheckEq(Flags(DS), '-', 'Bof and Eof are False');
  CheckEqBool(DS.Locate('name', ['Ada'], []), True, 'the current row is found again');
  CheckEq(TakeLog, ScrollSeq, 'and the events still fire');
  DS.RecNo := 3;
  ClearLog;
  CheckEqBool(DS.Locate('name', ['ada'], []), False, 'the compare is case-sensitive');
  CheckEq(TakeLog, '', 'not found: no events');
  CheckEqInt(DS.RecNo, 3, 'not found: the cursor is unchanged');
  CheckEqBool(DS.Locate('name', ['ADAM'], [loCaseInsensitive]), True, 'loCaseInsensitive');
  CheckEqInt(DS.RecNo, 2, 'row 2');
  CheckEqBool(DS.Locate('name', ['Bo'], []), False, 'a prefix does not match');
  CheckEqBool(DS.Locate('name', ['Bo'], [loPartialKey]), True, 'loPartialKey');
  CheckEqInt(DS.RecNo, 3, 'row 3');
  CheckEqBool(DS.Locate('name', ['ADA'], [loPartialKey]), False, 'loPartialKey is case-sensitive');
  CheckEqBool(DS.Locate('NAME', ['bO'], [loCaseInsensitive, loPartialKey]), True, 'both options; field names ignore case');
  CheckEqInt(DS.RecNo, 3, 'row 3 again');
  CheckEqBool(DS.Locate('name', ['Bobby'], [loPartialKey]), False, 'the key is longer than the value');
  CheckEqBool(DS.Locate('note', ['Second'], []), True, 'a memo field compares as a string');
  CheckEqInt(DS.RecNo, 3, 'memo: row 3');
  CheckEqBool(DS.Locate('note', ['first'], [loPartialKey]), True, 'memo: partial key');
  CheckEqInt(DS.RecNo, 1, 'memo: row 1');
  DS.Free;
end;

procedure TestLocateTyped;
var
  DS: TTestDataSet;
begin
  Section('locate: typed compare');
  DS := NewPeople;
  CheckEqBool(DS.Locate('id', ['4'], []), True, 'integer');
  CheckEqInt(DS.RecNo, 4, 'integer: row 4');
  CheckEqBool(DS.Locate('id', ['02'], []), True, 'the key is converted to the field type');
  CheckEqInt(DS.RecNo, 2, 'integer: row 2');
  CheckEqBool(DS.Locate('id', ['x'], []), False, 'an unconvertible key matches nothing');
  CheckEqBool(DS.Locate('amount', ['1'], [loPartialKey]), False, 'loPartialKey applies to string fields only');
  CheckEqBool(DS.Locate('amount', [FloatToStr(2.25)], []), True, 'float');
  CheckEqInt(DS.RecNo, 2, 'float: row 2');
  CheckEqBool(DS.Locate('amount', ['10'], []), True, 'float: an integral key');
  CheckEqInt(DS.RecNo, 4, 'float: row 4');
  CheckEqBool(DS.Locate('big', ['5000000000'], []), True, 'largeint');
  CheckEqInt(DS.RecNo, 1, 'largeint: row 1');
  CheckEqBool(DS.Locate('born', [DateToStr(Born2)], []), True, 'date');
  CheckEqInt(DS.RecNo, 3, 'date: row 3');
  CheckEqBool(DS.Locate('born', ['no date'], []), False, 'date: unconvertible');
  CheckEqBool(DS.Locate('flag', ['False'], []), True, 'boolean');
  CheckEqInt(DS.RecNo, 2, 'boolean: row 2');
  CheckEqBool(DS.Locate('flag', ['true'], []), True, 'boolean: the text ignores case');
  CheckEqInt(DS.RecNo, 1, 'boolean: row 1');
  CheckEqBool(DS.Locate('flag', ['0'], []), True, 'boolean: 0');
  CheckEqInt(DS.RecNo, 2, 'boolean: row 2 again');
  CheckEqBool(DS.Locate('flag', ['maybe'], []), False, 'boolean: unconvertible');
  CheckEqBool(DS.Locate('pic', ['x'], []), False, 'a blob value matches no text');

  Section('locate: null');
  CheckEqBool(DS.Locate('amount', [''], []), True, 'the empty key matches null');
  CheckEqInt(DS.RecNo, 3, 'the first row with a null amount');
  CheckEqBool(DS.Locate('name', [''], []), True, 'string field: null');
  CheckEqInt(DS.RecNo, 5, 'the row with a null name');
  CheckEqBool(DS.Locate('pic', [''], []), True, 'blob field: null');
  CheckEqInt(DS.RecNo, 2, 'the first row without a picture');
  CheckEqBool(DS.Locate('id', ['0'], []), False, 'null does not match a value');

  Section('locate: several fields');
  CheckEqBool(DS.Locate('name;amount', ['Ada', '10'], []), True, 'two fields');
  CheckEqInt(DS.RecNo, 4, 'the row matching both');
  CheckEqBool(DS.Locate('name;amount;flag', ['Ada', '10', 'False'], []), False, 'every field must match');
  CheckEqBool(DS.Locate('name;big', ['bob', ''], [loCaseInsensitive]), False, 'a value where null is asked');
  CheckEqBool(DS.Locate('name;big', ['ada', ''], [loCaseInsensitive]), True, 'a string and a null key');
  CheckEqInt(DS.RecNo, 4, 'row 4');
  ClearLog;
  CheckRaises(Calls.LocateTwoFieldsOneValue, 'EDatabaseError', 'Locate: number of values does not match number of fields',
    'fewer values than fields');
  CheckRaises(Calls.LocateOneFieldTwoValues, 'EDatabaseError', 'Locate: number of values does not match number of fields',
    'more values than fields');
  CheckRaises(Calls.LocateMissingField, 'EDatabaseError', 'Field ''nope'' not found', 'unknown field');
  CheckEq(TakeLog, '', 'the errors fire no events');
  CheckEqInt(DS.RecNo, 4, 'and leave the cursor');
  DS.Free;
end;

procedure TestLocateState;
var
  DS: TTestDataSet;
begin
  Section('locate: browse mode first');
  DS := NewDS(3);
  DS.Edit;
  DS.FieldByName('name').AsString := 'uno';
  ClearLog;
  CheckEqBool(DS.Locate('name', ['uno'], []), True, 'the posted value is found');
  CheckEq(TakeLog, '[deCheckBrowseMode dsEdit],' + PostEditSeq + ',' + ScrollSeq, 'a modified record is posted first');
  DS.Last;
  DS.Edit;
  ClearLog;
  CheckEqBool(DS.Locate('name', ['nine'], []), False, 'not found after an edit');
  CheckEq(TakeLog, '[deCheckBrowseMode dsEdit],BeforeCancel,[deUpdateState dsBrowse],[deDataSetChange dsBrowse],AfterCancel',
    'an unmodified record is cancelled; no scroll events');
  CheckEqInt(DS.RecNo, 3, 'the cursor is unchanged');
  DS.Free;

  DS := NewDS(0);
  CheckEqBool(DS.Locate('id', ['1'], []), False, 'empty dataset: not found');
  CheckEqBool(DS.Locate('id', [''], []), False, 'empty dataset: no row for a null key either');
  DS.Close;
  CheckRaises(Calls.LocateName, 'EDatabaseError', SClosed, 'Locate on a closed dataset');
  DS.Free;
end;

begin
  Calls := TCalls.Create;
  Born1 := EncodeDate(2000, 1, 2);
  Born2 := EncodeDate(1999, 12, 31);
  TestBookmarkValue;
  TestBookmarkValid;
  TestGotoBookmark;
  TestCompareBookmarks;
  TestLocateString;
  TestLocateTyped;
  TestLocateState;
  Calls.Free;
  WriteLn('failed checks: ', FailedChecks);
end.
