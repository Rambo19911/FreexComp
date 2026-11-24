// covers: LANG-683
program ComStream;

{ Card acceptance 1: a COM object of the operating system used through interfaces declared here. The memory stream
  comes from the shell helper library as a raw pointer that carries one reference; IStream(P) adopts it (LANG-682). }

uses
  SysUtils;

type
  TStatStg = record
    Name: PWideChar;
    Kind: Cardinal;
    Size: UInt64;
    MTime, CTime, ATime: Int64;
    Mode: Cardinal;
    LocksSupported: Cardinal;
    ClsId: TGUID;
    StateBits: Cardinal;
    Reserved: Cardinal;
  end;

  ISequentialStream = interface(IUnknown)
    ['{0C733A30-2A1C-11CE-ADE5-00AA0044773D}']
    function Read(Buffer: Pointer; Count: Cardinal; BytesRead: PCardinal): HResult; stdcall;
    function Write(Buffer: Pointer; Count: Cardinal; BytesWritten: PCardinal): HResult; stdcall;
  end;

  IStream = interface(ISequentialStream)
    ['{0000000C-0000-0000-C000-000000000046}']
    function Seek(Offset: Int64; Origin: Cardinal; NewPosition: PUInt64): HResult; stdcall;
    function SetSize(NewSize: UInt64): HResult; stdcall;
    function CopyTo(const Dest: IStream; Count: UInt64; BytesRead, BytesWritten: PUInt64): HResult; stdcall;
    function Commit(Flags: Cardinal): HResult; stdcall;
    function Revert: HResult; stdcall;
    function LockRegion(Offset, Count: UInt64; LockType: Cardinal): HResult; stdcall;
    function UnlockRegion(Offset, Count: UInt64; LockType: Cardinal): HResult; stdcall;
    function Stat(out StatStg: TStatStg; StatFlag: Cardinal): HResult; stdcall;
    function Clone(out Stream: IStream): HResult; stdcall;
  end;

const
  StatNoName = 1;

function SHCreateMemStream(Init: Pointer; Size: Cardinal): Pointer; stdcall; external 'shlwapi.dll';

procedure Run;
const
  Data: array[0..4] of Byte = (72, 101, 108, 108, 111);
var
  P: Pointer;
  S: IStream;
  Seq: ISequentialStream;
  Unk: IUnknown;
  Buf: array[0..7] of Byte;
  N: Cardinal;
  Pos: UInt64;
  St: TStatStg;
  I: Integer;
begin
  P := SHCreateMemStream(nil, 0);
  WriteLn('created ', P <> nil);
  S := IStream(P);                       // the variable holds a second reference

  N := 0;
  WriteLn('write ', S.Write(@Data[0], 5, @N), ' ', N);
  Pos := 99;
  WriteLn('seek ', S.Seek(0, 0, @Pos), ' ', Pos);
  WriteLn('stat ', S.Stat(St, StatNoName), ' size ', St.Size, ' name ', St.Name = nil);

  for I := 0 to 7 do
    Buf[I] := 0;
  N := 0;
  WriteLn('read ', S.Read(@Buf[0], 8, @N) >= 0, ' ', N);
  for I := 0 to Integer(N) - 1 do
    Write(Chr(Buf[I]));
  WriteLn;

  Unk := S as IUnknown;
  WriteLn('as IUnknown ', Unk <> nil);
  WriteLn('Supports IStream ', Supports(Unk, IStream));
  WriteLn('Supports ISequentialStream ', Supports(S, ISequentialStream, Seq));
  WriteLn('seek ', S.Seek(1, 0, nil));
  N := 0;
  WriteLn('read through the ancestor ', Seq.Read(@Buf[0], 2, @N), ' ', N, ' ', Chr(Buf[0]), Chr(Buf[1]));

  Seq := nil;
  Unk := nil;
  S := nil;
  WriteLn('final release ', IStream(P)._Release);
end;

begin
  Run;
  WriteLn('done');
end.
