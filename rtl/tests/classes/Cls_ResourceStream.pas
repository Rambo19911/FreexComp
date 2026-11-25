// phase: 8
// covers: RTL-1052, RTL-1053, RTL-1054
program Cls_ResourceStream;
{ TResourceStream (classes.md section 6.2): a read-only view of a Win32 resource of a loaded module. The resource is
  the application manifest every console program carries (type RT_MANIFEST, number 1): by number, by the name '#1',
  through HInstance and through module 0; Read, Seek, Position and SaveToStream; the memory is the module image (two
  streams over the same resource share it, nothing is allocated for it); a missing name and a missing number raise
  EResNotFound; Write and the Size setter raise EStreamError. }
{$POINTERMATH ON}
uses
  SysUtils, Classes, Windows, TestKit;

{ True when the Len bytes at P contain the single-byte text Text }
function ContainsText(P: PByte; Len: Integer; const Text: string): Boolean;
var
  I, J: Integer;
begin
  Result := False;
  for I := 0 to Len - Length(Text) do
  begin
    J := 0;
    while (J < Length(Text)) and (P[I + J] = Ord(Text[J + 1])) do
      Inc(J);
    if J = Length(Text) then
      Exit(True);
  end;
end;

var
  Before: NativeInt;
  S, ByName, Main: TResourceStream;
  M: TMemoryStream;
  Buf: array[0..63] of Byte;
  One: Byte;
  N: Integer;
begin
  Before := _LiveAllocCount;

  Section('the manifest of the program by number (RTL-1052)');
  S := TResourceStream.CreateFromID(HInstance, 1, RT_MANIFEST);
  Check(S.Size > 0, 'Size > 0');
  CheckEqI(0, S.Position, 'Position is 0');
  Check(S.Memory <> nil, 'Memory');
  Check(ContainsText(S.Memory, S.Size, 'assembly'), 'the bytes are text that contains assembly');
  CheckEqI(_LiveAllocCount, Before + 1, 'only the stream object was allocated: the bytes are not copied');

  Section('Read, Seek and Position (RTL-1054)');
  N := S.Read(Buf, SizeOf(Buf));
  CheckEqI(SizeOf(Buf), N, 'Read returns the bytes asked for');
  Check(CompareMem(@Buf[0], S.Memory, N), 'Read returns the bytes of the resource');
  CheckEqI(N, S.Position, 'Position after Read');
  CheckEqI(S.Size - 1, S.Seek(-1, soEnd), 'Seek from the end');
  CheckEqI(1, S.Read(One, 1), 'the last byte');
  CheckEqI(0, S.Read(One, 1), 'nothing after the end');
  CheckEqI(S.Size, S.Position, 'Position at the end');
  S.Position := 5;
  CheckEqI(5, S.Seek(0, soCurrent), 'Position can be set');
  M := TMemoryStream.Create;
  S.SaveToStream(M);
  CheckEqI(S.Size, M.Size, 'SaveToStream writes Size bytes');
  Check(CompareMem(M.Memory, S.Memory, S.Size), 'SaveToStream writes the bytes of the resource');
  M.Position := 0;
  S.Position := 0;
  CheckEqI(S.Size, M.CopyFrom(S, 0), 'CopyFrom reads the whole stream');
  M.Free;

  Section('the same resource by name and for module 0 (RTL-1052)');
  ByName := TResourceStream.Create(HInstance, '#1', RT_MANIFEST);
  CheckEqI(S.Size, ByName.Size, 'the size by name');
  Check(ByName.Memory = S.Memory, 'both streams view the same memory of the module image');
  CheckEqI(0, ByName.Position, 'Position of a new stream');
  Main := TResourceStream.CreateFromID(0, 1, RT_MANIFEST);
  Check(Main.Memory = S.Memory, 'module 0 is the program');
  Main.Free;
  ByName.Free;

  Section('a resource that does not exist (RTL-1053)');
  Expect('EResNotFound', 'Resource NOPE not found');
  try TResourceStream.Create(HInstance, 'NOPE', RT_RCDATA).Free; except on E: Exception do CheckRaises('a missing name', E.ClassName, E.Message); end;
  Expect('EResNotFound', 'Resource 4711 not found');
  try TResourceStream.CreateFromID(HInstance, 4711, RT_MANIFEST).Free; except on E: Exception do CheckRaises('a missing number', E.ClassName, E.Message); end;

  Section('the stream is read-only (RTL-1054)');
  Expect('EStreamError', 'Cannot write to a read-only resource stream');
  try S.Write(One, 1); except on E: Exception do CheckRaises('Write', E.ClassName, E.Message); end;
  try S.WriteBuffer(One, 1); except on E: Exception do CheckRaises('WriteBuffer', E.ClassName, E.Message); end;
  try S.Size := 10; except on E: Exception do CheckRaises('Size :=', E.ClassName, E.Message); end;
  Check(ContainsText(S.Memory, S.Size, 'assembly'), 'the resource is unchanged');

  S.Free;
  CheckEqI(Before, _LiveAllocCount, 'no block is left after the streams are freed');
  Done;
end.
