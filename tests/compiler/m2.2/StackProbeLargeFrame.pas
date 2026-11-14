// covers: CMP-611
program StackProbeLargeFrame;
// a 512 KiB local array: the prologue calls the compiler-emitted __chkstk (unit System), which touches every page

procedure Big;
var
  A: array[0..524287] of Byte;
  I: Integer;
  Sum: Int64;
begin
  I := 0;
  while I <= High(A) do
  begin
    A[I] := (I shr 12) and 255;
    I := I + 4096;
  end;
  A[High(A)] := 7;
  Sum := 0;
  I := 0;
  while I <= High(A) do
  begin
    Sum := Sum + A[I];
    I := I + 4096;
  end;
  Writeln(Sum + A[High(A)]);
end;

begin
  Big;
end.
