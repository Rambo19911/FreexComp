// covers: LANG-152, LANG-505, LANG-538
program UnitDottedQualified;
// unit Foo declares a record variable Bar (fields Tag, Baz.Value); the three-part unit Foo.Bar.Baz declares Value.
// In Foo.Bar.Baz.Value the longest leading sequence that names a used unit is the qualifier (LANG-538): unit
// Foo.Bar.Baz, not unit Foo with member Bar. Foo.Bar.Tag has no longer unit prefix: unit Foo, variable Bar, field Tag.
uses Foo, Foo.Bar.Baz;
begin
  Foo.Bar.Baz.Value := 3;
  Writeln(Foo.Bar.Baz.Value);
  Writeln(Value);
  Writeln(Deep);
  Writeln(Foo.Bar.Tag);
  Writeln(Bar.Baz.Value);
  Writeln(Foo.Bar.Baz.Deep);
end.
