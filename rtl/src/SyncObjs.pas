{ SyncObjs - synchronisation objects over the Win32 kernel objects (docs: rtl/other-units.md section 7). }
unit SyncObjs;

interface

uses
  Windows, SysUtils;

type
  TWaitResult = (wrSignaled, wrTimeout, wrAbandoned, wrError);

  { the base: Acquire and Release do nothing, WaitFor gives wrError }
  TSynchroObject = class(TObject)
  public
    procedure Acquire; virtual;
    procedure Release; virtual;
    function WaitFor(Timeout: Cardinal = INFINITE): TWaitResult; virtual;
  end;

  { re-entrant for the owning thread (RTL-662); spin count 4000 }
  TCriticalSection = class(TSynchroObject)
  protected
    FSection: TRTLCriticalSection;
  public
    constructor Create;
    destructor Destroy; override;
    procedure Acquire; override;
    procedure Release; override;
    procedure Enter;
    procedure Leave;
    function TryEnter: Boolean;
  end;

  { owns a kernel handle (closed by Destroy); WaitFor = WaitForSingleObject (RTL-660); Acquire = WaitFor(INFINITE) }
  THandleObject = class(TSynchroObject)
  protected
    FHandle: THandle;
    FLastError: Integer;
  public
    destructor Destroy; override;
    procedure Acquire; override;
    function WaitFor(Timeout: Cardinal = INFINITE): TWaitResult; override;
    property Handle: THandle read FHandle;
    property LastError: Integer read FLastError;
  end;

  TEvent = class(THandleObject)
  public
    { Name '' = unnamed; creation failure raises EOSError (RTL-661) }
    constructor Create(EventAttributes: Pointer; ManualReset, InitialState: Boolean; const Name: string); overload;
    { manual reset, not signaled, unnamed }
    constructor Create; overload;
    procedure SetEvent;
    procedure ResetEvent;
  end;

  TSimpleEvent = TEvent;

  TMutex = class(THandleObject)
  public
    constructor Create(InitialOwner: Boolean = False; const Name: string = '');
    { ReleaseMutex; by a thread that does not own the mutex it does nothing (not checked) }
    procedure Release; override;
  end;

  TSemaphore = class(THandleObject)
  public
    constructor Create(InitialCount, MaximumCount: Integer; const Name: string = '');
    { adds one to the count }
    procedure Release; override;
  end;

  { atomic operations: Increment/Decrement/Add return the new value, Exchange/CompareExchange the previous one }
  TInterlocked = class
  public
    class function Increment(var Target: Integer): Integer; overload;
    class function Increment(var Target: Int64): Int64; overload;
    class function Decrement(var Target: Integer): Integer; overload;
    class function Decrement(var Target: Int64): Int64; overload;
    class function Add(var Target: Integer; Increment: Integer): Integer; overload;
    class function Add(var Target: Int64; Increment: Int64): Int64; overload;
    class function Exchange(var Target: Integer; Value: Integer): Integer; overload;
    class function Exchange(var Target: Int64; Value: Int64): Int64; overload;
    class function Exchange(var Target: Pointer; Value: Pointer): Pointer; overload;
    class function CompareExchange(var Target: Integer; Value, Comparand: Integer): Integer; overload;
    class function CompareExchange(var Target: Int64; Value, Comparand: Int64): Int64; overload;
    class function CompareExchange(var Target: Pointer; Value, Comparand: Pointer): Pointer; overload;
  end;

implementation

{ nil for '', else the characters of Name }
function NameOrNil(const Name: string): PWideChar;
begin
  if Name = '' then
    Result := nil
  else
    Result := PWideChar(Name);
end;

{ RTL-661 }
function CheckHandle(H: THandle): THandle;
begin
  if H = 0 then
    RaiseLastOSError;
  Result := H;
end;

{ ---------------------------------------------------------------------------------------------- TSynchroObject }

procedure TSynchroObject.Acquire;
begin
end;

procedure TSynchroObject.Release;
begin
end;

function TSynchroObject.WaitFor(Timeout: Cardinal): TWaitResult;
begin
  Result := wrError;
end;

{ -------------------------------------------------------------------------------------------- TCriticalSection }

constructor TCriticalSection.Create;
begin
  InitializeCriticalSectionAndSpinCount(FSection, 4000);
end;

destructor TCriticalSection.Destroy;
begin
  DeleteCriticalSection(FSection);
  inherited Destroy;
end;

procedure TCriticalSection.Acquire;
begin
  EnterCriticalSection(FSection);
end;

procedure TCriticalSection.Release;
begin
  LeaveCriticalSection(FSection);
end;

procedure TCriticalSection.Enter;
begin
  EnterCriticalSection(FSection);
end;

procedure TCriticalSection.Leave;
begin
  LeaveCriticalSection(FSection);
end;

function TCriticalSection.TryEnter: Boolean;
begin
  Result := TryEnterCriticalSection(FSection);
end;

{ ----------------------------------------------------------------------------------------------- THandleObject }

destructor THandleObject.Destroy;
begin
  if FHandle <> 0 then
    CloseHandle(FHandle);
  inherited Destroy;
end;

procedure THandleObject.Acquire;
begin
  WaitFor(INFINITE);
end;

function THandleObject.WaitFor(Timeout: Cardinal): TWaitResult;
begin
  case WaitForSingleObject(FHandle, Timeout) of
    WAIT_OBJECT_0:
      Result := wrSignaled;
    WAIT_TIMEOUT:
      Result := wrTimeout;
    WAIT_ABANDONED:
      Result := wrAbandoned;
  else
    FLastError := Integer(Windows.GetLastError);
    Result := wrError;
  end;
end;

{ ------------------------------------------------------------------------------------------------------ TEvent }

constructor TEvent.Create(EventAttributes: Pointer; ManualReset, InitialState: Boolean; const Name: string);
begin
  FHandle := CheckHandle(CreateEventW(EventAttributes, ManualReset, InitialState, NameOrNil(Name)));
end;

constructor TEvent.Create;
begin
  Create(nil, True, False, '');
end;

procedure TEvent.SetEvent;
begin
  Windows.SetEvent(FHandle);
end;

procedure TEvent.ResetEvent;
begin
  Windows.ResetEvent(FHandle);
end;

{ ------------------------------------------------------------------------------------------------------ TMutex }

constructor TMutex.Create(InitialOwner: Boolean; const Name: string);
begin
  FHandle := CheckHandle(CreateMutexW(nil, InitialOwner, NameOrNil(Name)));
end;

procedure TMutex.Release;
begin
  ReleaseMutex(FHandle);
end;

{ -------------------------------------------------------------------------------------------------- TSemaphore }

constructor TSemaphore.Create(InitialCount, MaximumCount: Integer; const Name: string);
begin
  FHandle := CheckHandle(CreateSemaphoreW(nil, InitialCount, MaximumCount, NameOrNil(Name)));
end;

procedure TSemaphore.Release;
begin
  ReleaseSemaphore(FHandle, 1, nil);
end;

{ ------------------------------------------------------------------------------------------------ TInterlocked }

class function TInterlocked.Increment(var Target: Integer): Integer;
begin
  Result := AtomicIncrement(Target);
end;

class function TInterlocked.Increment(var Target: Int64): Int64;
begin
  Result := AtomicIncrement(Target);
end;

class function TInterlocked.Decrement(var Target: Integer): Integer;
begin
  Result := AtomicDecrement(Target);
end;

class function TInterlocked.Decrement(var Target: Int64): Int64;
begin
  Result := AtomicDecrement(Target);
end;

class function TInterlocked.Add(var Target: Integer; Increment: Integer): Integer;
begin
  Result := AtomicIncrement(Target, Increment);
end;

class function TInterlocked.Add(var Target: Int64; Increment: Int64): Int64;
begin
  Result := AtomicIncrement(Target, Increment);
end;

class function TInterlocked.Exchange(var Target: Integer; Value: Integer): Integer;
begin
  Result := AtomicExchange(Target, Value);
end;

class function TInterlocked.Exchange(var Target: Int64; Value: Int64): Int64;
begin
  Result := AtomicExchange(Target, Value);
end;

class function TInterlocked.Exchange(var Target: Pointer; Value: Pointer): Pointer;
begin
  Result := AtomicExchange(Target, Value);
end;

class function TInterlocked.CompareExchange(var Target: Integer; Value, Comparand: Integer): Integer;
begin
  Result := AtomicCmpExchange(Target, Value, Comparand);
end;

class function TInterlocked.CompareExchange(var Target: Int64; Value, Comparand: Int64): Int64;
begin
  Result := AtomicCmpExchange(Target, Value, Comparand);
end;

class function TInterlocked.CompareExchange(var Target: Pointer; Value, Comparand: Pointer): Pointer;
begin
  Result := AtomicCmpExchange(Target, Value, Comparand);
end;

end.
