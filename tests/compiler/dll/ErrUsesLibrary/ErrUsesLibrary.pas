// covers: LANG-542
program ErrUsesLibrary;
// a library file is not a unit: naming it in a uses clause is E2217 at the entry, by name and with 'in'
uses SomeLib, Other in 'OtherLib.pas';
begin
end.
