// covers: LANG-502, LANG-510, LANG-535
program UnitUsesIn;
// "in 'path'" names the file relative to the program's directory (either separator) and wins over a same-named
// unit in the program's directory (LANG-510 step 1)
uses
  Foo in 'sub/Foo.pas',
  Bar in 'sub\deeper\Bar.pas';
begin
  Writeln(FooFrom);
  Writeln(Bar.Twice(21));
end.
