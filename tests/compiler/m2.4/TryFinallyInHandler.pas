// covers: LANG-810, LANG-821, CMP-392
program TryFinallyInHandler;

{ a finally part inside a handler uses the handler variable: in a routine (it lives in the frame record) and in the
  program block (C-091: in module storage, the program block has no frame) }

type
  ETagged = class
    Tag: string;
    constructor Create(const T: string);
    destructor Destroy; override;
  end;

constructor ETagged.Create(const T: string); begin Tag := T end;
destructor ETagged.Destroy; begin Writeln('destroy ', Tag); inherited end;

procedure InRoutine;
var
  Count: Integer;
begin
  Count := 0;
  try
    raise ETagged.Create('routine');
  except
    on E: ETagged do
      try
        Count := Count + 1;
      finally
        Writeln('routine finally sees ', E.Tag, ' ', Count);
      end;
  end;
end;

var
  Before: NativeInt;
begin
  Before := _LiveAllocCount;
  InRoutine;
  try
    raise ETagged.Create('program');
  except
    on E: ETagged do
      try
        Writeln('handler');
      finally
        Writeln('program finally sees ', E.Tag);
      end;
  end;
  Writeln('leaked: ', _LiveAllocCount - Before);
end.