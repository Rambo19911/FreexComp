// covers: LANG-711, LANG-716, CMP-385
// C-228: TBase<TFoo> is reached by GenZ only through TDerived<TFoo>'s signature; TFoo is local to the program, so only the
// program's module may define the instance (its RTTI names the program's internal TFoo.$rtti).
program GenericInstanceOfProgramType;

uses
  GenZ;

type
  TFoo = class
  end;

var
  B: TDerived<TFoo>;
  P: TDerived<TBoxZ<TFoo>>;

begin
  B := TDerived<TFoo>.Create;
  B.Take(B);
  WriteLn(B.Get = nil);
  WriteLn(B.Current = nil);
  B.Free;
  // an instance whose argument is another unit's instance over a program type: also only the program defines it
  P := TDerived<TBoxZ<TFoo>>.Create;
  P.Take(P);
  WriteLn(P.Get = nil, ' ', P.Current = nil);
  P.Free;
end.
