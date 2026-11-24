// covers: RTL-960, RTL-968
program useheap;
// host and library allocate and free strings: each side counts its own blocks only, and a memory manager installed
// in the library is not the host's

uses SysUtils;

function TextLength(Count: Integer): Integer; stdcall; external 'heaplib.dll';
function Keep(Count: Integer): Integer; stdcall; external 'heaplib.dll';
procedure Drop; stdcall; external 'heaplib.dll';
function LiveBlocks: NativeInt; stdcall; external 'heaplib.dll';
procedure InstallManager; stdcall; external 'heaplib.dll';
procedure RestoreManager; stdcall; external 'heaplib.dll';
function ManagerCalls: NativeInt; stdcall; external 'heaplib.dll';
function ManagerIsSet: Integer; stdcall; external 'heaplib.dll';

var
  HostStart, LibraryStart, CallsBefore: NativeInt;
  Length1000: Integer;
  Mine: string;
  Before, After: TMemoryManager;
begin
  HostStart := _LiveAllocCount;
  LibraryStart := LiveBlocks;

  Length1000 := TextLength(1000);
  Writeln('TextLength(1000) = ', Length1000);
  Writeln('after a string inside the library: host ', _LiveAllocCount - HostStart, ', library ', LiveBlocks - LibraryStart);
  Writeln('the library keeps ', Keep(500), ' characters');
  Writeln('while the library keeps a string: host ', _LiveAllocCount - HostStart, ', library ', LiveBlocks - LibraryStart);
  SetLength(Mine, 300);
  Writeln('while both keep a string: host ', _LiveAllocCount - HostStart, ', library ', LiveBlocks - LibraryStart);
  Drop;
  Writeln('after the library dropped its string: host ', _LiveAllocCount - HostStart, ', library ', LiveBlocks - LibraryStart);
  Mine := '';
  Writeln('after the host dropped its string: host ', _LiveAllocCount - HostStart, ', library ', LiveBlocks - LibraryStart);

  GetMemoryManager(Before);
  InstallManager;
  GetMemoryManager(After);
  Writeln('host manager unchanged by SetMemoryManager in the library: ', CompareMem(@Before, @After, SizeOf(TMemoryManager)));
  Writeln('host IsMemoryManagerSet: ', IsMemoryManagerSet);
  Writeln('library IsMemoryManagerSet: ', ManagerIsSet = 1);
  CallsBefore := ManagerCalls;
  SetLength(Mine, 400);
  Mine := '';
  Writeln('host allocations reach the library manager: ', ManagerCalls <> CallsBefore);
  TextLength(100);
  Writeln('library allocations reach the library manager: ', ManagerCalls > CallsBefore);
  RestoreManager;
  Writeln('library IsMemoryManagerSet after restoring: ', ManagerIsSet = 1);
  Writeln('TextLength(10) = ', TextLength(10));
end.
