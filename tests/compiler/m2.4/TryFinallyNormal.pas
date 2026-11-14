// covers: LANG-810
program TryFinallyNormal;

{ the finally part runs after normal completion, sees the routine's variables (also Result and managed locals) and
  nested try statements run their finally parts innermost first }

var
  S: string;
  Count: Integer;

function Twice(N: Integer): Integer;
var
  T: string;
begin
  T := 'twice';
  try
    Result := N * 2;
    T := T + '!';
  finally
    Writeln('finally of ', T, ' with ', Result);
  end;
end;

var
  Before: NativeInt;
begin
  Before := _LiveAllocCount;
  Count := 0;
  try
    Count := Count + 1;
    S := 'body';
  finally
    Count := Count + 10;
    Writeln('finally: ', S, ' ', Count);
  end;
  Writeln(Twice(21));
  try
    try
      Writeln('inner body');
    finally
      Writeln('inner finally');
    end;
  finally
    Writeln('outer finally');
  end;
  S := '';
  Writeln('leaked: ', _LiveAllocCount - Before);
end.