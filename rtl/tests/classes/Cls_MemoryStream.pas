// phase: 4
// covers: RTL-431, RTL-435
// opc-run: tempdir
program Cls_MemoryStream;
{ Memory streams (classes.md section 6.2): TMemoryStream write/read, capacity in multiples of 8192 growing at least
  1.25 times, reading past the end returns fewer bytes, seeking past the end then writing zero-fills the gap,
  a negative seek gives position 0 and Seek returns the new position (RTL-435), SetSize, Clear, LoadFromStream from
  position 0, SaveToStream, SaveToFile/LoadFromFile in the temp folder; TBytesStream over a TBytes; TStringStream
  with UTF-8 and UTF-16 (DataString, ReadString, WriteString); CopyFrom between memory streams (RTL-431). }
{$POINTERMATH ON}
uses
  SysUtils, Classes, TestKit;

type
  TPeek = class(TMemoryStream)
  public
    function Cap: NativeInt;
  end;

function TPeek.Cap: NativeInt;
begin
  Result := Capacity;
end;

var
  M: TPeek;
  N: TMemoryStream;
  BS: TBytesStream;
  SS: TStringStream;
  Buf: array[0..9999] of Byte;
  B: TBytes;
  I: Integer;
  Dir: string;
begin
  Dir := IncludeTrailingPathDelimiter(ParamStr(1));

  Section('TMemoryStream');
  M := TPeek.Create;
  for I := 0 to 9999 do
    Buf[I] := I mod 251;
  M.WriteBuffer(Buf, 100);
  CheckEqI(8192, M.Cap, 'first capacity is one block');
  M.WriteBuffer(Buf, 9000);
  CheckEqI(16384, M.Cap, 'next multiple of 8192');
  CheckEqI(9100, M.Size, 'size');
  M.WriteBuffer(Buf, 9000);
  CheckEqI(24576, M.Cap, 'grows to cover the write');
  M.Position := 0;
  M.WriteBuffer(Buf, 10);
  M.Size := 20481;
  CheckEqI(20481, M.Size, 'SetSize');
  CheckEqI(24576, M.Cap, 'SetSize rounds the capacity');
  M.Position := 20000;
  CheckEqI(481, M.Read(Buf, 1000), 'read past the end returns fewer bytes');
  CheckEqI(0, M.Read(Buf, 10), 'read at the end');
  CheckEqI(0, M.Seek(-5, soBeginning), 'negative seek gives 0');
  CheckEqI(20476, M.Seek(-5, soEnd), 'Seek returns the new position');
  M.Clear;
  CheckEqI(0, M.Size, 'Clear size');
  CheckEqI(0, M.Cap, 'Clear capacity');
  Check(M.Memory = nil, 'Clear frees the memory');
  Buf[0] := 7;
  M.Position := 4;
  M.WriteBuffer(Buf, 1);
  CheckEqI(5, M.Size, 'write after a gap');
  CheckEqI(0, PByte(M.Memory)[1], 'gap zero-filled');
  CheckEqI(7, PByte(M.Memory)[4], 'written byte');

  Section('load, save, copy');
  N := TMemoryStream.Create;
  M.Position := 3;
  N.LoadFromStream(M);
  CheckEqI(5, N.Size, 'LoadFromStream reads from position 0');
  CheckEqI(0, N.Position, 'position after load');
  N.Position := N.Size;
  M.SaveToStream(N);
  CheckEqI(10, N.Size, 'SaveToStream appends all');
  M.SaveToFile(Dir + 'm.bin');
  N.LoadFromFile(Dir + 'm.bin');
  CheckEqI(5, N.Size, 'file round trip');
  CheckEqI(7, PByte(N.Memory)[4], 'file content');
  N.Clear;
  M.Position := 1;
  CheckEqI(2, N.CopyFrom(M, 2), 'CopyFrom count');
  CheckEqI(5, N.CopyFrom(M, 0), 'CopyFrom 0');
  CheckEqI(7, N.Size, 'copied size');
  N.Free;
  M.Free;

  Section('TBytesStream');
  SetLength(B, 3);
  B[0] := 1; B[1] := 2; B[2] := 3;
  BS := TBytesStream.Create(B);
  CheckEqI(3, BS.Size, 'size of the bytes');
  BS.Position := 1;
  BS.ReadBuffer(Buf, 2);
  CheckEqI(3, Buf[1], 'reads the bytes');
  Buf[0] := 9;
  BS.WriteBuffer(Buf, 1);
  CheckEqI(4, BS.Size, 'grows');
  CheckEqI(9, BS.Bytes[3], 'Bytes holds the written byte');
  Check(Length(BS.Bytes) >= 4, 'Bytes covers the size');
  BS.Free;

  Section('TStringStream');
  SS := TStringStream.Create('aé');
  CheckEqI(3, SS.Size, 'UTF-8 by default');
  CheckEqS('aé', SS.DataString, 'DataString');
  SS.Position := SS.Size;
  SS.WriteString('z€');
  CheckEqS('aéz€', SS.DataString, 'WriteString appends');
  SS.Position := 1;
  CheckEqS('é', SS.ReadString(2), 'ReadString');
  CheckEqS('z€', SS.ReadString(100), 'ReadString to the end');
  CheckEqS('', SS.ReadString(5), 'ReadString at the end');
  SS.Free;
  SS := TStringStream.Create('hi', TEncoding.Unicode);
  CheckEqI(4, SS.Size, 'UTF-16');
  CheckEqS('hi', SS.DataString, 'UTF-16 DataString');
  Check(SS.Encoding = TEncoding.Unicode, 'Encoding');
  SS.Free;
  SS := TStringStream.Create;
  CheckEqI(0, SS.Size, 'empty');
  SS.Free;
  Done;
end.
