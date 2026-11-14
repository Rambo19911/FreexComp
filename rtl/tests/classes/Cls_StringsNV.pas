// phase: 4
// covers: RTL-424
program Cls_StringsNV;
{ TStrings name/value API and delimited text (classes.md section 5.1): Names, Values (assigning '' deletes, a new
  name appends Name=Value), ValueFromIndex, IndexOfName, a custom NameValueSeparator; DelimitedText get quoting
  empty items and items with the quote char, the delimiter or (unless StrictDelimiter) blanks; set splitting on the
  delimiter and on blank runs, un-quoting, skipping leading blanks, a trailing delimiter giving an empty item;
  StrictDelimiter; CommaText ignores the properties (RTL-424). }
uses
  SysUtils, Classes, TestKit;

function Lines(L: TStrings): string;
var
  I: Integer;
begin
  Result := IntToStr(L.Count) + ':';
  for I := 0 to L.Count - 1 do
    Result := Result + '[' + L[I] + ']';
end;

var
  L: TStringList;
begin
  L := TStringList.Create;

  Section('names and values');
  L.Text := 'a=1'#10'B=2=3'#10'plain'#10'=empty';
  CheckEqS('a', L.Names[0], 'Names');
  CheckEqS('', L.Names[2], 'Names without separator');
  CheckEqS('', L.Names[3], 'empty name');
  CheckEqS('2=3', L.Values['b'], 'Values case-insensitive, first separator');
  CheckEqS('', L.Values['plain'], 'line without separator is not a name');
  CheckEqS('', L.Values['zz'], 'missing');
  CheckEqI(1, L.IndexOfName('B'), 'IndexOfName');
  CheckEqI(-1, L.IndexOfName('plain'), 'IndexOfName without separator');
  CheckEqS('1', L.ValueFromIndex[0], 'ValueFromIndex');
  CheckEqS('', L.ValueFromIndex[2], 'ValueFromIndex without separator');
  L.Values['a'] := '9';
  CheckEqS('a=9', L[0], 'Values replaces');
  L.Values['new'] := 'v';
  CheckEqS('new=v', L[L.Count - 1], 'Values appends');
  L.Values['B'] := '';
  CheckEqS('4:[a=9][plain][=empty][new=v]', Lines(L), 'Values '''' deletes');
  L.Values['missing'] := '';
  CheckEqI(4, L.Count, 'deleting a missing name does nothing');
  L.ValueFromIndex[0] := 'x';
  CheckEqS('a=x', L[0], 'ValueFromIndex set');
  L.ValueFromIndex[0] := '';
  CheckEqS('3:[plain][=empty][new=v]', Lines(L), 'ValueFromIndex '''' deletes');
  L.NameValueSeparator := ':';
  L.Text := 'k:v'#10'k2=v2';
  CheckEqS('v', L.Values['K'], 'custom separator');
  CheckEqS('', L.Values['k2'], 'default separator no longer used');
  L.NameValueSeparator := '=';

  Section('DelimitedText get');
  L.Clear;
  L.Add('plain');
  L.Add('');
  L.Add('with space');
  L.Add('say "hi"');
  L.Add('a,b');
  L.Add('tab'#9);
  CheckEqS('plain,"","with space","say ""hi""","a,b","tab'#9'"', L.DelimitedText, 'quoting');
  CheckEqS(L.DelimitedText, L.CommaText, 'CommaText');
  L.StrictDelimiter := True;
  CheckEqS('plain,"",with space,"say ""hi""","a,b",tab'#9, L.DelimitedText, 'strict: blanks unquoted');
  L.Delimiter := ';';
  L.QuoteChar := '''';
  CheckEqS('plain;'''';with space;say "hi";a,b;tab'#9, L.DelimitedText, 'custom delimiter and quote');
  CheckEqS('plain,"","with space","say ""hi""","a,b","tab'#9'"', L.CommaText, 'CommaText ignores the properties');
  L.Clear;
  CheckEqS('', L.DelimitedText, 'empty list');
  L.Delimiter := ',';
  L.QuoteChar := '"';
  L.StrictDelimiter := False;

  Section('DelimitedText set');
  L.DelimitedText := 'a,b c,"d e","f""g",,h';
  CheckEqS('7:[a][b][c][d e][f"g][][h]', Lines(L), 'blanks split, quotes, empty item');
  L.DelimitedText := '  x ,  y';
  CheckEqS('2:[x][y]', Lines(L), 'leading blanks skipped');
  L.DelimitedText := 'a,';
  CheckEqS('2:[a][]', Lines(L), 'trailing delimiter');
  L.DelimitedText := '';
  CheckEqS('0:', Lines(L), 'empty');
  L.StrictDelimiter := True;
  L.DelimitedText := ' a b, c';
  CheckEqS('2:[ a b][ c]', Lines(L), 'strict: only the delimiter splits');
  L.Delimiter := ';';
  L.CommaText := 'p q,"r,s"';
  CheckEqS('3:[p][q][r,s]', Lines(L), 'CommaText set ignores the properties');
  CheckEqS(';', L.Delimiter, 'properties restored');
  CheckEqB(True, L.StrictDelimiter, 'StrictDelimiter restored');
  L.Free;
  Done;
end.
