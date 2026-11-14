// covers: LANG-840, CMP-393
program ExceptionThroughFrames;

{ an exception raised five frames deep passes through routines with managed locals, value parameters and temporaries:
  all of them are released on the way (leak counter) }

type
  EDeep = class
    Depth: Integer;
    constructor Create(D: Integer);
  end;

  TInts = array of Integer;

constructor EDeep.Create(D: Integer); begin Depth := D end;

function Level(N: Integer; Text: string): Integer;
var
  Local: string;
  Values: TInts;
begin
  Local := Text + '/' + Chr(Ord('0') + N);
  SetLength(Values, N + 1);
  Values[N] := N;
  if N = 5 then
    raise EDeep.Create(N);
  Result := Level(N + 1, Local + '.') + Values[N];
end;

var
  Before: NativeInt;
  R: Integer;
begin
  Before := _LiveAllocCount;
  R := 0;
  try
    R := Level(1, 'root');
  except
    on E: EDeep do Writeln('caught at depth ', E.Depth);
  end;
  Writeln('R = ', R);
  Writeln('leaked: ', _LiveAllocCount - Before);
end.