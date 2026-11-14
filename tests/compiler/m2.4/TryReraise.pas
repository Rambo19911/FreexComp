// covers: LANG-807, LANG-823
program TryReraise;

{ 'raise;' propagates the object being handled; it is destroyed once, after the last handler }

type
  ECount = class
    N: Integer;
    destructor Destroy; override;
  end;

destructor ECount.Destroy; begin Writeln('destroy ', N); inherited end;

var
  Saved: TObject;
  Before: NativeInt;
begin
  Before := _LiveAllocCount;
  try
    try
      raise ECount.Create;
    except
      on E: ECount do
      begin
        E.N := 1;
        Saved := E;
        Writeln('inner sees ', E.N);
        raise;
      end;
    end;
  except
    on E: ECount do
    begin
      Inc(E.N);
      Writeln('outer sees ', E.N, ' same=', E = Saved);
    end;
  end;
  try
    try
      try
        raise ECount.Create;
      except
        Writeln('level 1');
        raise;
      end;
    except
      Writeln('level 2');
      raise;
    end;
  except
    on E: ECount do Writeln('level 3 ', E.N);
  end;
  Writeln('leaked: ', _LiveAllocCount - Before);
end.