// covers: RTL-131, RTL-132
// opc-run: variants 1 2 3 4 5 6 7 8 9 a
program Sys_RunError;
{ Variant 1: RunError(201). Variant 2: RunError without a code is error 0. Output written before the error is
  flushed first; the address is 16 upper-case hex digits and not zero (the return address of _RunError, C-173).
  Variant 3: a range error raised through the helper _RangeError reports the user's call site (R-199): the address
  ErrorProc receives lies inside Fail, the routine with the failing index, not inside the helper.
  Variants 4 to a (R-208): errors raised inside System itself report the user's call site too - 4 GetMem and 5
  SetLength of a dynamic array when the memory manager returns nil (203), 6 SetLength with a negative length (201),
  7 FreeMem when the memory manager refuses the block (204), 8 interface "as" interface and 9 object "as" interface
  that is not supported (228), and variant a: Free of a TInterfacedObject that still has references (204, reported
  inside the user's destructor, whose prologue calls BeforeDestruction). Each address lies inside the routine named
  by FailStart, not inside System. }
{$R+}
type
  IFoo = interface
    ['{F00F00F0-0001-0002-0304-050607080910}']
  end;

  IBar = interface
    ['{BA7BA7BA-0001-0002-0304-050607080910}']
  end;

  TMiB = array[0..1048575] of Byte;

  TFoo = class(TInterfacedObject, IFoo)
  end;

  THeld = class(TInterfacedObject, IFoo)
  public
    destructor Destroy; override;
  end;

var
  P: PChar;
  A: array[0..3] of Integer;
  FailStart: NativeUInt;
  Old, Trap: TMemoryManager;
  Refused: Pointer;
  Blocks: array of TMiB;
  Keep: IFoo;

const
  Huge = NativeInt(1) shl 41;

{ an empty routine emitted right before THeld.Destroy: the range checked for variant a starts here }
procedure HeldMarker;
begin
end;

destructor THeld.Destroy;
begin
  inherited Destroy;
end;

function TrapGetMem(Size: NativeInt): Pointer;
begin
  if Size >= Huge then
    Result := nil
  else
    Result := Old.GetMem(Size);
end;

function TrapAllocMem(Size: NativeInt): Pointer;
begin
  if Size >= Huge then
    Result := nil
  else
    Result := Old.AllocMem(Size);
end;

function TrapFreeMem(P: Pointer): Integer;
begin
  if P = Refused then
    Result := 1
  else
    Result := Old.FreeMem(P);
end;

procedure InstallTrap;
begin
  GetMemoryManager(Old);
  Trap := Old;
  Trap.GetMem := TrapGetMem;
  Trap.AllocMem := TrapAllocMem;
  Trap.FreeMem := TrapFreeMem;
  SetMemoryManager(Trap);
end;

procedure Fail(I: Integer);
begin
  A[I] := 1;
  Writeln('not reached');
end;

procedure FailGetMem(Size: NativeInt);
var
  Q: Pointer;
begin
  GetMem(Q, Size);
  Writeln('not reached');
end;

{ N blocks of 1 MiB: a length within Integer whose size the memory manager refuses }
procedure FailSetLength(N: Integer);
begin
  SetLength(Blocks, N);
  Writeln('not reached');
end;

procedure FailFreeMem;
begin
  FreeMem(Refused);
  Writeln('not reached');
end;

procedure FailIntfAs(const F: IFoo);
var
  B: IBar;
begin
  B := F as IBar;
  Writeln('not reached');
end;

procedure FailObjAs(O: TObject);
var
  B: IBar;
begin
  B := O as IBar;
  Writeln('not reached');
end;

procedure Hook(Code: Integer; Addr: Pointer);
begin
  Writeln('error ', Code, ' inside Fail ',
    (NativeUInt(Addr) > FailStart) and (NativeUInt(Addr) < FailStart + 256), ', = ErrorAddr ', Addr = ErrorAddr);
end;

var
  H: THeld;
begin
  P := CmdLine;
  while P^ <> #0 do
    Inc(P);
  Dec(P);
  Writeln('before ', ErrorAddr = nil);
  Write('pending without line end');
  if (P^ <> '1') and (P^ <> '2') then
  begin
    Writeln;
    ErrorProc := Hook;
  end;
  case P^ of
    '3':
      begin
        FailStart := NativeUInt(@Fail);
        Fail(10);
      end;
    '4':
      begin
        InstallTrap;
        FailStart := NativeUInt(@FailGetMem);
        FailGetMem(Huge);
      end;
    '5':
      begin
        InstallTrap;
        FailStart := NativeUInt(@FailSetLength);
        FailSetLength(1 shl 21);
      end;
    '6':
      begin
        FailStart := NativeUInt(@FailSetLength);
        FailSetLength(-1);
      end;
    '7':
      begin
        GetMem(Refused, 16);
        InstallTrap;
        FailStart := NativeUInt(@FailFreeMem);
        FailFreeMem;
      end;
    '8':
      begin
        FailStart := NativeUInt(@FailIntfAs);
        FailIntfAs(TFoo.Create);
      end;
    '9':
      begin
        FailStart := NativeUInt(@FailObjAs);
        FailObjAs(TFoo.Create);
      end;
    'a':
      begin
        H := THeld.Create;
        Keep := H;
        FailStart := NativeUInt(@HeldMarker);
        H.Free;
      end;
  end;
  if P^ = '1' then
    RunError(201)
  else
    RunError;
  Writeln('not reached');
end.
