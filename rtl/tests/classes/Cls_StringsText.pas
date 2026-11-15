// phase: 4
// covers: RTL-420, RTL-421, RTL-425
program Cls_StringsText;
{ TStrings core (classes.md section 5.1) through TStringList: Text get with and without TrailingLineBreak and with a
  custom LineBreak (RTL-420); Text set splitting at CR LF, lone LF and lone CR, a custom LineBreak as the only
  separator, no extra line for a final break (RTL-421); update counting (SetUpdateState at the outermost
  Begin/EndUpdate only, Text set inside one update); Add/Insert/Append/AddStrings/Assign/Equals/Exchange/Move/
  IndexOf/IndexOfObject; index errors (RTL-425). }
uses
  SysUtils, Classes, TestKit;

type
  TCounting = class(TStringList)
  protected
    procedure SetUpdateState(Updating: Boolean); override;
  end;

var
  Log: string;

procedure TCounting.SetUpdateState(Updating: Boolean);
begin
  if Updating then
    Log := Log + 'begin;'
  else
    Log := Log + 'end;';
  inherited SetUpdateState(Updating);
end;

function Lines(L: TStrings): string;
var
  I: Integer;
begin
  Result := IntToStr(L.Count) + ':';
  for I := 0 to L.Count - 1 do
    Result := Result + '[' + L[I] + ']';
end;

function IndexError(L: TStrings; Kind, N: Integer): string;
var
  S: string;
begin
  Result := '';
  try
    case Kind of
      0: S := L[N];
      1: L.Delete(N);
      2: L.Insert(N, 'x');
      3: L.Objects[N] := nil;
    end;
  except
    on E: EStringListError do
      Result := E.Message;
  end;
end;

var
  L, M: TStringList;
  C: TCounting;
  O1, O2: TObject;
  Arr: array of string;
  Strs: TArray<string>;
begin
  L := TStringList.Create;

  Section('Text get (RTL-420)');
  CheckEqS('', L.Text, 'empty');
  L.Add('a');
  L.Add('b');
  CheckEqS('a'#13#10'b'#13#10, L.Text, 'trailing break');
  L.TrailingLineBreak := False;
  CheckEqS('a'#13#10'b', L.Text, 'no trailing break');
  L.TrailingLineBreak := True;
  L.LineBreak := '|';
  CheckEqS('a|b|', L.Text, 'custom LineBreak');
  L.LineBreak := sLineBreak;

  Section('Text set (RTL-421)');
  L.Text := 'one'#13#10'two'#10'three'#13'four';
  CheckEqS('4:[one][two][three][four]', Lines(L), 'CR LF, LF, CR');
  L.Text := 'a'#13#10#13#10;
  CheckEqS('2:[a][]', Lines(L), 'final break adds no line');
  L.Text := 'a'#10#13'b';
  CheckEqS('3:[a][][b]', Lines(L), 'LF CR is two breaks');
  L.Text := '';
  CheckEqS('0:', Lines(L), 'empty text');
  L.Text := #13#10;
  CheckEqS('1:[]', Lines(L), 'a lone break');
  L.LineBreak := '::';
  L.Text := 'x::y'#13#10'z::';
  CheckEqS('2:[x][y'#13#10'z]', Lines(L), 'custom LineBreak is the only separator');
  L.LineBreak := sLineBreak;

  Section('update counting');
  C := TCounting.Create;
  C.BeginUpdate;
  C.BeginUpdate;
  C.Add('x');
  C.EndUpdate;
  C.EndUpdate;
  CheckEqS('begin;end;', Log, 'outermost only');
  Log := '';
  C.Text := 'p'#10'q';
  CheckEqS('begin;end;', Log, 'Text set inside one update');
  Log := '';
  SetLength(Arr, 2);
  Arr[0] := 'r';
  Arr[1] := 's';
  C.AddStrings(Arr);           { an open array constructor here hits a compiler overload bug (reported) }
  CheckEqS('begin;end;', Log, 'AddStrings inside one update');
  CheckEqS('4:[p][q][r][s]', Lines(C), 'AddStrings array');

  Section('core members');
  O1 := TObject.Create;
  O2 := TObject.Create;
  L.Clear;
  CheckEqI(0, L.Add('b'), 'Add index');
  L.AddObject('c', O1);
  L.Insert(0, 'a');
  L.InsertObject(1, 'x', O2);
  L.Append('d');
  CheckEqS('5:[a][x][b][c][d]', Lines(L), 'insert and append');
  Check(L.Objects[3] = O1, 'AddObject');
  CheckEqI(1, L.IndexOfObject(O2), 'IndexOfObject');
  CheckEqI(2, L.IndexOf('B'), 'IndexOf is case-insensitive');
  CheckEqI(-1, L.IndexOf('zz'), 'IndexOf absent');
  L.Exchange(0, 3);
  CheckEqS('5:[c][x][b][a][d]', Lines(L), 'Exchange');
  Check(L.Objects[0] = O1, 'object travels with Exchange');
  L.Move(0, 4);
  CheckEqS('5:[x][b][a][d][c]', Lines(L), 'Move');
  Check(L.Objects[4] = O1, 'object travels with Move');
  L[0] := 'y';
  Check(L.Objects[0] = O2, 'Put keeps the object');
  M := TStringList.Create;
  M.Delimiter := ';';
  M.Assign(L);
  CheckEqS(Lines(L), Lines(M), 'Assign copies lines');
  Check(M.Objects[4] = O1, 'Assign copies objects');
  CheckEqS(',', M.Delimiter, 'Assign copies Delimiter');
  CheckEqB(True, M.Equals(L), 'Equals');
  M[0] := 'Y';
  CheckEqB(False, M.Equals(L), 'Equals is ordinal');
  M.Delete(0);
  CheckEqB(False, M.Equals(L), 'Equals counts');
  CheckEqI(8, L.Capacity, 'capacity grows in the steps of RTL-416');

  Section('ToStringArray');
  Strs := L.ToStringArray;
  CheckEqI(5, Length(Strs), 'one item per line');
  CheckEqS('y|b|a|d|c', Strs[0] + '|' + Strs[1] + '|' + Strs[2] + '|' + Strs[3] + '|' + Strs[4], 'lines in order');
  Strs[0] := 'changed';
  CheckEqS('y', L[0], 'a copy, not a view');
  M.Clear;
  CheckEqI(0, Length(M.ToStringArray), 'empty list gives an empty array');

  Section('index errors (RTL-425)');
  CheckEqS('List index out of bounds (5)', IndexError(L, 0, 5), 'read');
  CheckEqS('List index out of bounds (-1)', IndexError(L, 1, -1), 'Delete');
  CheckEqS('List index out of bounds (6)', IndexError(L, 2, 6), 'Insert past Count');
  CheckEqS('', IndexError(L, 2, 5), 'Insert at Count');
  CheckEqS('List index out of bounds (9)', IndexError(L, 3, 9), 'Objects');
  O1.Free;
  O2.Free;
  M.Free;
  C.Free;
  L.Free;
  Done;
end.
