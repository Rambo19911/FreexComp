// phase: 7
// covers: RTL-682, RTL-683, RTL-684
// opc-run: tempdir
program Ini_Preserve;
{ IniFiles syntax and rewriting (other-units.md section 9): comments (";" and "#"), blank lines and lines without
  "=" are kept at their position on rewrite and ignored by Read*; section names between the first "[" and the last
  "]" trimmed; lines before the first section belong to section ''; duplicate sections are merged (first position
  wins), duplicate identifiers: the first wins on read and writes update it (RTL-682, RTL-683); new keys go to the
  end of their section (before its trailing blank lines), new sections to the end of the file after one empty
  line; CRLF output (RTL-684). }
uses
  SysUtils, Classes, IniFiles, TestKit;

var
  Dir: string;

function FileText(const FileName: string): string;
var
  L: TStringList;
begin
  L := TStringList.Create;
  try
    L.LoadFromFile(FileName);
    L.LineBreak := '|';
    Result := L.Text;
  finally
    L.Free;
  end;
end;

procedure WriteText(const FileName, Text: string);
var
  L: TStringList;
begin
  L := TStringList.Create;
  try
    L.Text := Text;
    L.SaveToFile(FileName);
  finally
    L.Free;
  end;
end;

function Joined(L: TStrings): string;
begin
  L.LineBreak := ',';
  Result := L.Text;
end;

const
  Original =
    'top=1'#10 +
    '; leading comment'#10 +
    #10 +
    '[ One ]'#10 +
    '  a = 1'#10 +
    '# hash comment'#10 +
    'just text'#10 +
    'a=2'#10 +
    #10 +
    '[Two]'#10 +
    'b=3'#10 +
    #10 +
    '[one]'#10 +
    'c=4'#10 +
    '[x[y]z]'#10 +
    'k=v'#10;

var
  Ini: TMemIniFile;
  L: TStringList;
begin
  Dir := IncludeTrailingPathDelimiter(ParamStr(1));
  L := TStringList.Create;
  WriteText(Dir + 'p.ini', Original);
  Ini := TMemIniFile.Create(Dir + 'p.ini');

  Section('syntax (RTL-682, RTL-683)');
  CheckEqS('1', Ini.ReadString('', 'top', ''), 'lines before the first section belong to section ''''');
  CheckEqS('1', Ini.ReadString('One', 'a', ''), 'trimmed section and key, first duplicate wins');
  CheckEqS('4', Ini.ReadString('ONE', 'c', ''), 'duplicate sections are merged');
  CheckEqS('v', Ini.ReadString('x[y]z', 'k', ''), 'name between the first "[" and the last "]"');
  CheckEqS('', Ini.ReadString('One', 'just text', ''), 'lines without "=" are ignored');
  CheckEqS('', Ini.ReadString('One', '# hash comment', ''), 'comments are ignored');
  Ini.ReadSections(L);
  CheckEqS('One,Two,x[y]z,', Joined(L), 'merged sections keep the first position');
  Ini.ReadSection('one', L);
  CheckEqS('a,c,', Joined(L), 'identifiers of the merged section, duplicates once');

  Section('rewrite (RTL-684)');
  Ini.WriteString('One', 'a', '10');
  Ini.WriteString('One', 'new', 'n');
  Ini.WriteString('Two', 'b2', '5');
  Ini.WriteString('Three', 'd', '6');
  Ini.WriteString('', 'top2', '2');
  Ini.UpdateFile;
  CheckEqS(
    'top=1|top2=2|; leading comment||' +
    '[ One ]|a=10|# hash comment|just text|a=2|new=n||' +
    '[Two]|b=3|b2=5||' +
    '[one]|c=4|' +
    '[x[y]z]|k=v||' +
    '[Three]|d=6|', FileText(Dir + 'p.ini'), 'comments, blank lines and order preserved');
  Ini.Free;

  Section('erase and delete keep the rest');
  Ini := TMemIniFile.Create(Dir + 'p.ini');
  Ini.EraseSection('one');
  Ini.DeleteKey('Two', 'b');
  Ini.UpdateFile;
  CheckEqS('top=1|top2=2|; leading comment||[Two]|b2=5||[x[y]z]|k=v||[Three]|d=6|', FileText(Dir + 'p.ini'),
    'EraseSection removes every occurrence, DeleteKey one line');
  Ini.Free;
  L.Free;
  Done;
end.
