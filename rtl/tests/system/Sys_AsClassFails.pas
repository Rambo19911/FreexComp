// covers: LANG-651, ABI-102
program Sys_AsClassFails;
{ _AsClass (ABI section 9.6) on an object whose class does not descend from the target: _InvalidCast, runtime error
  219 (message on stderr, exit code 219), through the compiler's `as` and nothing after it runs. The first casts
  succeed: nil and a matching class pass through unchanged. }

type
  TAnimal = class
  end;
  TDog = class(TAnimal)
  end;
  TCar = class
  end;

var
  O: TObject;
  A: TAnimal;
begin
  O := nil;
  Writeln('nil as TDog = nil: ', (O as TDog) = nil);
  O := TDog.Create;
  A := O as TAnimal;
  Writeln('as ancestor: ', A.ClassName);
  O.Free;
  O := TCar.Create;
  Writeln('casting ', O.ClassName, ' to TAnimal');
  A := O as TAnimal;
  Writeln('not reached ', A.ClassName);
end.