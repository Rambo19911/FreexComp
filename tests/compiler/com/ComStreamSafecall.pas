// covers: LANG-686, LANG-687
program ComStreamSafecall;

{ Card acceptance 1 and 3 against foreign code: the stream of the operating system through a second declaration of its
  interface whose methods are safecall. The results equal those of the stdcall declaration, and a call the stream
  rejects raises ESafecallException (the program uses SysUtils, not ComObj). }

uses
  SysUtils;

type
  IStream = interface(IUnknown)
    ['{0000000C-0000-0000-C000-000000000046}']
    function Read(Buffer: Pointer; Count: Cardinal; BytesRead: PCardinal): HResult; stdcall;
    function Write(Buffer: Pointer; Count: Cardinal; BytesWritten: PCardinal): HResult; stdcall;
    function Seek(Offset: Int64; Origin: Cardinal; NewPosition: PUInt64): HResult; stdcall;
    function SetSize(NewSize: UInt64): HResult; stdcall;
  end;

  IStreamSafe = interface(IUnknown)
    ['{0000000C-0000-0000-C000-000000000046}']
    procedure Read(Buffer: Pointer; Count: Cardinal; BytesRead: PCardinal); safecall;
    procedure Write(Buffer: Pointer; Count: Cardinal; BytesWritten: PCardinal); safecall;
    procedure Seek(Offset: Int64; Origin: Cardinal; NewPosition: PUInt64); safecall;
    procedure SetSize(NewSize: UInt64); safecall;
  end;

function SHCreateMemStream(Init: Pointer; Size: Cardinal): Pointer; stdcall; external 'shlwapi.dll';

procedure Run;
const
  Data: array[0..4] of Byte = (72, 101, 108, 108, 111);
var
  P: Pointer;
  Raw: IStream;
  Safe: IStreamSafe;
  Buf: array[0..7] of Byte;
  N: Cardinal;
  Pos: UInt64;
  I: Integer;
begin
  P := SHCreateMemStream(nil, 0);
  Raw := IStream(P);
  Safe := IStreamSafe(P);
  WriteLn('same object ', Pointer(Raw) = Pointer(Safe));

  N := 0;
  Safe.Write(@Data[0], 5, @N);
  WriteLn('write ', N);
  Pos := 99;
  Safe.Seek(0, 0, @Pos);
  WriteLn('seek ', Pos);
  for I := 0 to 7 do
    Buf[I] := 0;
  N := 0;
  Safe.Read(@Buf[0], 5, @N);
  Write('read ', N, ' ');
  for I := 0 to Integer(N) - 1 do
    Write(Chr(Buf[I]));
  WriteLn;

  Pos := 99;
  WriteLn('stdcall seek ', Raw.Seek(2, 0, @Pos), ' ', Pos);
  Safe.Seek(0, 2, @Pos);
  WriteLn('safecall seek to the end ', Pos);

  WriteLn('rejected call');
  Pos := 7;
  WriteLn('  stdcall result negative ', Raw.Seek(0, 99, @Pos) < 0);
  try
    Safe.Seek(0, 99, @Pos);
    WriteLn('  not reached');
  except
    on E: Exception do
      WriteLn('  ', E.ClassName);
  end;
  Safe.Seek(0, 0, @Pos);
  WriteLn('still usable ', Pos);

  Safe := nil;
  Raw := nil;
  WriteLn('final release ', IStream(P)._Release);
end;

begin
  Run;
  WriteLn('done');
end.
