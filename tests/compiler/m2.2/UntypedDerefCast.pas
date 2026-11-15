// covers: LANG-265, LANG-290
program UntypedDerefCast;

type
  TThing = class
    Value: Integer;
  end;

  TPair = record
    A, B: Integer;
  end;

var
  Slot: TThing;
  N: Integer;
  Pair: TPair;
  P: Pointer;
  T: TThing;
begin
  { p^ of an untyped Pointer as the operand of a typecast: a variable of the cast type at that address }
  T := TThing.Create;
  T.Value := 42;
  P := @Slot;
  TThing(P^) := T;
  Writeln(Slot = T, ' ', Slot.Value);
  Writeln(TThing(P^).Value);
  TObject(P^) := nil;
  Writeln(Slot = nil);
  T.Free;

  P := @N;
  Integer(P^) := 7;
  Integer(P^) := Integer(P^) * 6;
  Writeln(N);
  Inc(Integer(P^));
  Writeln(N);

  P := @Pair;
  TPair(P^).A := 1;
  TPair(P^).B := 2;
  Writeln(Pair.A, ' ', Pair.B, ' ', TPair(P^).A + TPair((P)^).B);
end.
