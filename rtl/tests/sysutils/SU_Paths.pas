// covers: RTL-261
program SU_Paths;
{ sysutils.md section 8.2: path string functions (pure; ExpandFileName is checked relative to the current directory). }
uses
  SysUtils, TestKit;

procedure Q(const Name, Input, Expected, Actual: string);
begin
  CheckEqS(Expected, Actual, Name + '(' + QuoteStr(Input) + ')');
end;

procedure Table;
begin
  Section('ExtractFilePath / ExtractFileDir');
  Q('ExtractFilePath', 'C:\dir\file.txt', 'C:\dir\', ExtractFilePath('C:\dir\file.txt'));
  Q('ExtractFilePath', 'C:file.txt', 'C:', ExtractFilePath('C:file.txt'));
  Q('ExtractFilePath', 'file.txt', '', ExtractFilePath('file.txt'));
  Q('ExtractFilePath', 'a/b/c', 'a/b/', ExtractFilePath('a/b/c'));
  Q('ExtractFilePath', '\\srv\share\x', '\\srv\share\', ExtractFilePath('\\srv\share\x'));
  Q('ExtractFilePath', '', '', ExtractFilePath(''));
  Q('ExtractFileDir', 'C:\dir\file.txt', 'C:\dir', ExtractFileDir('C:\dir\file.txt'));
  Q('ExtractFileDir', 'C:\file.txt', 'C:\', ExtractFileDir('C:\file.txt'));
  Q('ExtractFileDir', '\file.txt', '\', ExtractFileDir('\file.txt'));
  Q('ExtractFileDir', 'a/b/c', 'a/b', ExtractFileDir('a/b/c'));
  Q('ExtractFileDir', 'C:x', 'C:', ExtractFileDir('C:x'));
  Q('ExtractFileDir', 'x', '', ExtractFileDir('x'));

  Section('ExtractFileName / ExtractFileExt');
  Q('ExtractFileName', 'C:\dir\file.txt', 'file.txt', ExtractFileName('C:\dir\file.txt'));
  Q('ExtractFileName', 'C:file.txt', 'file.txt', ExtractFileName('C:file.txt'));
  Q('ExtractFileName', 'a/b', 'b', ExtractFileName('a/b'));
  Q('ExtractFileName', 'C:\dir\', '', ExtractFileName('C:\dir\'));
  Q('ExtractFileName', 'plain', 'plain', ExtractFileName('plain'));
  Q('ExtractFileExt', 'C:\dir\file.txt', '.txt', ExtractFileExt('C:\dir\file.txt'));
  Q('ExtractFileExt', 'a.tar.gz', '.gz', ExtractFileExt('a.tar.gz'));
  Q('ExtractFileExt', 'C:\dir.d\file', '', ExtractFileExt('C:\dir.d\file'));
  Q('ExtractFileExt', 'name.', '.', ExtractFileExt('name.'));
  Q('ExtractFileExt', '.hidden', '.hidden', ExtractFileExt('.hidden'));
  Q('ExtractFileExt', 'a/b.c/d', '', ExtractFileExt('a/b.c/d'));
  Q('ExtractFileExt', '', '', ExtractFileExt(''));

  Section('ExtractFileDrive');
  Q('ExtractFileDrive', 'C:\dir', 'C:', ExtractFileDrive('C:\dir'));
  Q('ExtractFileDrive', 'c:', 'c:', ExtractFileDrive('c:'));
  Q('ExtractFileDrive', '\\srv\share\dir\f', '\\srv\share', ExtractFileDrive('\\srv\share\dir\f'));
  Q('ExtractFileDrive', '\\srv\share', '\\srv\share', ExtractFileDrive('\\srv\share'));
  Q('ExtractFileDrive', '//srv/share/x', '//srv/share', ExtractFileDrive('//srv/share/x'));
  Q('ExtractFileDrive', '\dir', '', ExtractFileDrive('\dir'));
  Q('ExtractFileDrive', 'dir\f', '', ExtractFileDrive('dir\f'));
  Q('ExtractFileDrive', '\\srv', '', ExtractFileDrive('\\srv'));

  Section('ChangeFileExt / ChangeFilePath');
  Q('ChangeFileExt', 'a\b.txt', 'a\b.bak', ChangeFileExt('a\b.txt', '.bak'));
  Q('ChangeFileExt', 'a\b', 'a\b.bak', ChangeFileExt('a\b', '.bak'));
  Q('ChangeFileExt', 'a\b.txt', 'a\b', ChangeFileExt('a\b.txt', ''));
  Q('ChangeFileExt', 'a.d\b', 'a.d\bx', ChangeFileExt('a.d\b', 'x'));
  Q('ChangeFilePath', 'C:\x\f.txt', 'D:\y\f.txt', ChangeFilePath('C:\x\f.txt', 'D:\y'));
  Q('ChangeFilePath', 'f.txt', 'D:\y\f.txt', ChangeFilePath('f.txt', 'D:\y\'));

  Section('trailing delimiters');
  Q('IncludeTrailingPathDelimiter', 'C:\a', 'C:\a\', IncludeTrailingPathDelimiter('C:\a'));
  Q('IncludeTrailingPathDelimiter', 'C:\a\', 'C:\a\', IncludeTrailingPathDelimiter('C:\a\'));
  Q('IncludeTrailingPathDelimiter', 'a/', 'a/', IncludeTrailingPathDelimiter('a/'));
  Q('IncludeTrailingPathDelimiter', '', '\', IncludeTrailingPathDelimiter(''));
  Q('ExcludeTrailingPathDelimiter', 'C:\a\', 'C:\a', ExcludeTrailingPathDelimiter('C:\a\'));
  Q('ExcludeTrailingPathDelimiter', 'a//', 'a/', ExcludeTrailingPathDelimiter('a//'));
  Q('ExcludeTrailingPathDelimiter', 'a', 'a', ExcludeTrailingPathDelimiter('a'));
  CheckEqB(True, IsPathDelimiter('a\b', 2), 'IsPathDelimiter \');
  CheckEqB(True, IsPathDelimiter('a/b', 2), 'IsPathDelimiter /');
  CheckEqB(False, IsPathDelimiter('a\b', 1), 'IsPathDelimiter not');
  CheckEqB(False, IsPathDelimiter('a\b', 9), 'IsPathDelimiter beyond');

  Section('relative paths');
  CheckEqB(True, IsRelativePath('a\b'), 'IsRelativePath a\b');
  CheckEqB(True, IsRelativePath('C:a'), 'IsRelativePath C:a');
  CheckEqB(False, IsRelativePath('C:\a'), 'IsRelativePath C:\a');
  CheckEqB(False, IsRelativePath('\a'), 'IsRelativePath \a');
  CheckEqB(False, IsRelativePath('/a'), 'IsRelativePath /a');
  Q('ExtractRelativePath', 'C:\a\b\x.txt', 'x.txt', ExtractRelativePath('C:\a\b\', 'C:\a\b\x.txt'));
  Q('ExtractRelativePath', 'C:\a\c\x.txt', '..\c\x.txt', ExtractRelativePath('C:\a\b\', 'C:\a\c\x.txt'));
  Q('ExtractRelativePath', 'C:\x.txt', '..\..\x.txt', ExtractRelativePath('C:\a\b\', 'C:\x.txt'));
  Q('ExtractRelativePath', 'c:\A\B\y', 'y', ExtractRelativePath('C:\a\b\base.txt', 'c:\A\B\y'));
  Q('ExtractRelativePath', 'D:\a\x', 'D:\a\x', ExtractRelativePath('C:\a\', 'D:\a\x'));
  CheckEqB(True, SameFileName('C:\A.TXT', 'c:\a.txt'), 'SameFileName');
  CheckEqB(False, SameFileName('a', 'b'), 'SameFileName differs');
end;

var
  Cur: string;
begin
  Table;
  Section('ExpandFileName');
  Cur := IncludeTrailingPathDelimiter(ExpandFileName('.'));
  CheckEqS(Cur + 'sub\f.txt', ExpandFileName('sub/x/../f.txt'), 'ExpandFileName relative');
  CheckEqS('C:\a\c', ExpandFileName('C:\a\b\..\c'), 'ExpandFileName absolute');
  Done;
end.
