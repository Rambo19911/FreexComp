// covers: CMP-365
program RttiSharedAcrossModules;

{ C-199: the RTTI block of 'string' is needed by U1 (InitTable of TC) and by this program (record TQ); both modules
  emit it linkonce_odr, which links only with a COFF comdat. }

uses U1;

type
  TQ = record
    S: string;
  end;

procedure X;
var
  Q: TQ;
begin
  Q.S := 'a';
  Writeln(Q.S);
end;

var
  C: TC;
begin
  X;
  C := TC.Create;
  C.S := 'b';
  Writeln(C.S);
  C.Free;
end.
