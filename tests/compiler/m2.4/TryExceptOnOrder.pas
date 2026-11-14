// covers: LANG-821, LANG-823, LANG-824
program TryExceptOnOrder;

{ handlers are tried in source order, an ancestor's handler catches descendants, the else part catches the rest;
  the object is destroyed after its handler }

type
  EBase = class
    Name: string;
    constructor Create(const N: string);
    destructor Destroy; override;
  end;

  EMid = class(EBase)
  end;

  ELeaf = class(EMid)
  end;

  EOther = class
  end;

constructor EBase.Create(const N: string); begin Name := N end;
destructor EBase.Destroy; begin Writeln('  destroy ', Name); inherited end;

procedure Test(K: Integer);
begin
  try
    case K of
      1: raise ELeaf.Create('leaf');
      2: raise EMid.Create('mid');
      3: raise EBase.Create('base');
      4: raise EOther.Create;
    end;
    Writeln('no exception');
  except
    on E: ELeaf do Writeln('ELeaf handler: ', E.Name);
    on E: EMid do Writeln('EMid handler: ', E.Name, ' is ELeaf=', E is ELeaf);
    on EBase do Writeln('EBase handler');
  else
    Writeln('else part: ', ExceptObject.ClassName);
  end;
end;

var
  K: Integer;
  Before: NativeInt;
begin
  Before := _LiveAllocCount;
  for K := 0 to 4 do
    Test(K);
  Writeln('outside a handler ExceptObject = nil: ', ExceptObject = nil);
  Writeln('leaked: ', _LiveAllocCount - Before);
end.