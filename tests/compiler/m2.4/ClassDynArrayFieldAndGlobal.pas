// covers: CMP-362, CMP-365, ABI-072
program ClassDynArrayFieldAndGlobal;

{ C-190: the anonymous types 'array of string' and 'array[0..1] of string' are spelt alike in a class field, a
  record field, a global and a local; each gets one RTTI block in the module and all of them are finalised
  through it (leak counter). }

type
  TLeaf = class
    Tags: array of string;
    Pair: array[0..1] of string;
  end;

  TRec = record
    Tags: array of string;
  end;

var
  A: array of string;
  R: TRec;
  Leaf: TLeaf;
  Before: NativeInt;

procedure Fill;
var
  L: array of string;
begin
  SetLength(L, 2);
  L[0] := 'lo';
  L[1] := 'cal';
  Writeln(L[0], L[1], ' ', Length(L));
end;

begin
  Before := _LiveAllocCount;
  SetLength(A, 2);
  A[0] := 'glo';
  A[1] := 'bal';
  SetLength(R.Tags, 1);
  R.Tags[0] := 'rec';
  Leaf := TLeaf.Create;
  SetLength(Leaf.Tags, 3);
  Leaf.Tags[2] := 'leaf';
  Leaf.Pair[1] := 'pair';
  Writeln(A[0], A[1], ' ', R.Tags[0], ' ', Leaf.Tags[2], ' ', Length(Leaf.Tags), ' ', Leaf.Pair[1]);
  Fill;
  Leaf.Free;
  A := nil;
  R.Tags := nil;
  Writeln('leaked: ', _LiveAllocCount - Before);
end.
