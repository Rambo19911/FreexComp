// covers: ABI-172
program ErrAtomicUnaligned;
type
  TPacked = packed record
    Tag: Byte;
    Count: Integer;
    Link: Pointer;
  end;
var
  R: TPacked;
  N: Integer;
begin
  N := AtomicIncrement(R.Count);
  AtomicExchange(R.Link, nil);
  N := AtomicIncrement(N);
end.
