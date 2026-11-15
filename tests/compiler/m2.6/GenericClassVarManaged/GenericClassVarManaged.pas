// covers: LANG-717, LANG-523
program GenericClassVarManaged;

uses
  UProbe, UGen, UUser;

begin
  TRegistry<string>.Add('main');
  TRegistry<Integer>.Add(7);
  TRegistry<Integer>.Title := Copy('integers!', 1, 8);
  Writeln(TRegistry<string>.Title, ' ', TRegistry<string>.Count, ' ', TRegistry<string>.Items[0], ' ', TRegistry<string>.Items[1]);
  Writeln(TRegistry<Integer>.Title, ' ', TRegistry<Integer>.Count, ' ', TRegistry<Integer>.Items[0]);
  Writeln(UserCount);
end.
