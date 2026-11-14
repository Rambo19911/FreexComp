// covers: LANG-510, CMP-625
// opc: -U lib -U lib2
program UnitSearchPath;
// Shapes is only in lib, Extra only in lib2; Where is in the main directory and in lib (the main directory wins,
// LANG-510 step 3 before 4); Tools is in lib and lib2 (the -U directories are searched in order)
uses Shapes, Where, Tools, Extra;
begin
  Writeln(Area(3, 4));
  Writeln(WhereFrom);
  Writeln(ToolsFrom);
  Writeln(ExtraFrom);
end.
