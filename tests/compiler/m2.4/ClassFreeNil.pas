// covers: ABI-077, LANG-634
program ClassFreeNil;

type
  TNode = class
    Next: TNode;
    destructor Destroy; override;
  end;

destructor TNode.Destroy;
begin
  Writeln('destroying');
  Next.Free;                               { nil for the last node }
  inherited;
end;

var
  A: TNode;
  Before: NativeInt;
begin
  Before := _LiveAllocCount;
  A := nil;
  A.Free;
  Writeln('nil freed');
  A := TNode.Create;
  A.Next := TNode.Create;
  A.Free;
  Writeln('leaked: ', _LiveAllocCount - Before);
end.
