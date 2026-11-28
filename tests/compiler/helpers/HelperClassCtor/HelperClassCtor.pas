// covers: LANG-995, CMP-1490
program HelperClassCtor;

{ class constructors and class destructors of helpers (LANG-995): the helper's class constructor has run before the program
  starts although no method of the helper is ever called; the order follows LANG-789 and LANG-790 (c) }

uses
  UReg;

begin
  Writeln('main ', Integer.Calls);
end.
