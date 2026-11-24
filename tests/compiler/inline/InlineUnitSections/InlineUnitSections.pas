// covers: LANG-567, LANG-565
program InlineUnitSections;

{ Inline declarations in a unit's initialization and finalization lists: variables of those lists live for the section,
  a managed variable in a nested block is released at the end of the block. }

uses InlineUnitSectionsU;

begin
  Writeln('main ', Total);
  var Local := NewGuard('main local');
  begin
    var Nested := NewGuard('main nested');
    Nested.Touch;
  end;
  Local.Touch;
  Writeln('end of main');
end.
