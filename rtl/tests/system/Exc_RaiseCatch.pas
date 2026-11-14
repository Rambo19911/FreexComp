// covers: RTL-141, RTL-160, RTL-161, ABI-120, ABI-121, ABI-122, ABI-123
program Exc_RaiseCatch;
{ The exception runtime of System (rtl-abi.md 9.7, system.md section 14) seen from Pascal code: handler selection
  order, re-raise of the same object, the object destroyed exactly once (destructor counter), ExceptObject /
  ExceptAddr / RaiseList inside and outside handlers, raise E at Addr, nested exceptions inside a handler,
  AcquireExceptionObject / ReleaseExceptionObject, raise E of the object being handled, raise nil = error 204
  (through ErrorProc), and the helpers _Raise / _CurrentExcept called directly. Leak checks: RTL-064 counter. }
uses TestKit;

type
  EBase = class
    Id: Integer;
    Text: string;
    constructor Create(AId: Integer);
    destructor Destroy; override;
  end;
  EMiddle = class(EBase);
  ELeaf = class(EMiddle);
  EOther = class(EBase);

  ERuntime = class
    Code: Integer;
  end;

var
  Created, Destroyed: Integer;

constructor EBase.Create(AId: Integer);
begin
  Id := AId;
  Text := 'exception ' + IntStr(AId);
  Created := Created + 1;
end;

destructor EBase.Destroy;
begin
  Destroyed := Destroyed + 1;
  inherited Destroy;
end;

procedure Thrower(Id: Integer);
begin
  case Id mod 3 of
    0: raise ELeaf.Create(Id);
    1: raise EOther.Create(Id);
  else
    raise EMiddle.Create(Id);
  end;
end;

function Classify(Id: Integer): string;
begin
  try
    Thrower(Id);
    Result := 'none';
  except
    on E: ELeaf do Result := 'leaf ' + IntStr(E.Id);
    on E: EMiddle do Result := 'middle ' + IntStr(E.Id);
    on E: EBase do Result := 'base ' + IntStr(E.Id);
  end;
end;

{ in a routine of its own: the result strings are temporaries released when it ends }
procedure SelectionCases;
begin
  CheckEqS('leaf 3', Classify(3), 'first matching on wins (leaf before middle)');
  CheckEqS('base 4', Classify(4), 'unrelated class falls to the base handler');
  CheckEqS('middle 5', Classify(5), 'middle');
end;

procedure TestSelection;
var
  Before: NativeInt;
begin
  Section('handler selection');
  Before := _LiveAllocCount;
  Created := 0;
  Destroyed := 0;
  SelectionCases;
  CheckEqI(3, Created, 'created');
  CheckEqI(3, Destroyed, 'each destroyed once at the handler end');
  CheckEqI(0, _LiveAllocCount - Before, 'leaked');
end;

procedure TestReraise;
var
  Seen: TObject;
  Same: Boolean;
  Before: NativeInt;
begin
  Section('re-raise');
  Before := _LiveAllocCount;
  Created := 0;
  Destroyed := 0;
  Seen := nil;
  Same := False;
  try
    try
      raise EMiddle.Create(7);
    except
      on E: EBase do
      begin
        Seen := E;
        raise;
      end;
    end;
  except
    on E: EMiddle do
    begin
      Same := Pointer(E) = Pointer(Seen);
      CheckEqI(0, Destroyed, 'not destroyed by the inner handler');
    end;
  end;
  CheckEqB(True, Same, 'outer handler gets the same object');
  CheckEqI(1, Destroyed, 'destroyed once after the outer handler');
  CheckEqI(0, _LiveAllocCount - Before, 'leaked');
end;

procedure TestRaiseSameObject;
var
  Before: NativeInt;
  Id: Integer;
begin
  Section('raise E of the handled object');
  Before := _LiveAllocCount;
  Created := 0;
  Destroyed := 0;
  Id := 0;
  try
    try
      raise ELeaf.Create(8);
    except
      on E: ELeaf do
        raise E;
    end;
  except
    on E: ELeaf do
    begin
      Id := E.Id;
      CheckEqI(0, Destroyed, 'still alive in the outer handler');
    end;
  end;
  CheckEqI(8, Id, 'outer handler');
  CheckEqI(1, Destroyed, 'destroyed once');
  CheckEqI(0, _LiveAllocCount - Before, 'leaked');
end;

procedure TestExceptObject;
var
  Inner, Outer, InnerAddr: Pointer;
  AfterInner: TObject;
  ListIn: Pointer;
  Marker: Integer;
begin
  Section('ExceptObject, ExceptAddr, RaiseList');
  Check(ExceptObject = nil, 'ExceptObject nil outside a handler');
  Check(ExceptAddr = nil, 'ExceptAddr nil outside a handler');
  Check(RaiseList = nil, 'RaiseList nil outside a handler');
  Check(_CurrentExcept = nil, '_CurrentExcept nil outside a handler');
  Inner := nil;
  Outer := nil;
  InnerAddr := nil;
  AfterInner := nil;
  ListIn := nil;
  try
    raise EBase.Create(1) at @Marker;
  except
    on E: EBase do
    begin
      Outer := Pointer(ExceptObject);
      Check(Outer = Pointer(E), 'ExceptObject = the handled object');
      Check(_CurrentExcept = Pointer(E), '_CurrentExcept = ExceptObject');
      Check(ExceptAddr = @Marker, 'raise at Addr gives ExceptAddr');
      ListIn := RaiseList;
      Check(ListIn <> nil, 'RaiseList inside a handler');
      try
        raise EOther.Create(2) at @Inner;
      except
        on X: EOther do
        begin
          Inner := Pointer(ExceptObject);
          InnerAddr := ExceptAddr;
          Check(RaiseList <> ListIn, 'nested handler has its own entry');
        end;
      end;
      AfterInner := ExceptObject;
      Check(ExceptAddr = @Marker, 'outer ExceptAddr again after the nested handler');
    end;
  end;
  Check((Inner <> nil) and (Inner <> Outer), 'nested handler sees the nested object');
  Check(InnerAddr = @Inner, 'nested ExceptAddr');
  Check(Pointer(AfterInner) = Outer, 'outer object current again after the nested handler');
  Check(ExceptObject = nil, 'nil again after the handlers');
  Check(RaiseList = nil, 'RaiseList nil again');
end;

procedure TestAcquire;
var
  Kept: Pointer;
  Before: NativeInt;
begin
  Section('AcquireExceptionObject');
  Before := _LiveAllocCount;
  Created := 0;
  Destroyed := 0;
  Check(AcquireExceptionObject = nil, 'nil outside a handler');
  ReleaseExceptionObject;
  Kept := nil;
  try
    raise EBase.Create(9);
  except
    Kept := AcquireExceptionObject;
  end;
  CheckEqI(0, Destroyed, 'acquired object survives its handler');
  CheckEqI(9, EBase(Kept).Id, 'still usable');
  EBase(Kept).Free;
  CheckEqI(1, Destroyed, 'freed by its owner');
  try
    raise EBase.Create(10);
  except
    AcquireExceptionObject;
    ReleaseExceptionObject;
  end;
  CheckEqI(2, Destroyed, 'released object is destroyed by the handler end');
  CheckEqI(0, _LiveAllocCount - Before, 'leaked');
end;

{ ErrorProc turns runtime error 204 of raise nil into a catchable object }
procedure RuntimeToException(Code: Integer; Addr: Pointer);
var
  R: ERuntime;
begin
  R := ERuntime.Create;
  R.Code := Code;
  raise R;
end;

procedure TestRaiseNil;
var
  Code: Integer;
  O: TObject;
begin
  Section('raise nil');
  ErrorProc := RuntimeToException;
  Code := 0;
  O := nil;
  try
    raise O;
  except
    on R: ERuntime do Code := R.Code;
  end;
  ErrorProc := nil;
  CheckEqI(204, Code, 'raise nil is runtime error 204');
end;

procedure TestRaiseHelper;
var
  Before: NativeInt;
  Got: Integer;
begin
  Section('_Raise called directly');
  Before := _LiveAllocCount;
  Got := 0;
  try
    _Raise(Pointer(EBase.Create(11)), @Got);
  except
    on E: EBase do
    begin
      Got := E.Id;
      Check(ExceptAddr = @Got, 'Addr argument is ExceptAddr');
    end;
  end;
  CheckEqI(11, Got, 'caught');
  CheckEqI(0, _LiveAllocCount - Before, 'leaked');
end;

procedure TestMany;
var
  I, Caught: Integer;
  Before: NativeInt;
begin
  Section('many exceptions');
  Before := _LiveAllocCount;
  Created := 0;
  Destroyed := 0;
  Caught := 0;
  for I := 1 to 1000 do
    try
      Thrower(I);
    except
      on E: EBase do Caught := Caught + 1;
    end;
  CheckEqI(1000, Caught, 'all caught');
  CheckEqI(1000, Destroyed, 'all destroyed');
  CheckEqI(0, _LiveAllocCount - Before, 'leaked');
end;

begin
  TestSelection;
  TestReraise;
  TestRaiseSameObject;
  TestExceptObject;
  TestAcquire;
  TestRaiseNil;
  TestRaiseHelper;
  TestMany;
  Done;
end.
