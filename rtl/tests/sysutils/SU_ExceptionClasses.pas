// covers: RTL-210, RTL-211, ABI-125
program SU_ExceptionClasses;
{ sysutils.md section 2.1/2.2: the hierarchy (parent of every class plus InheritsFrom checks), the constructors and
  properties of Exception, RaiseOuterException / InnerException, and the pre-allocated heap exceptions. }
uses
  SysUtils, TestKit;

procedure Parent(C: ExceptClass; const ParentName: string);
begin
  CheckEqS(ParentName, C.ClassParent.ClassName, C.ClassName + ' < ' + ParentName);
end;

procedure Hierarchy;
begin
  Section('hierarchy');
  Parent(Exception, 'TObject');
  Parent(EAbort, 'Exception');
  Parent(EHeapException, 'Exception');
  Parent(EOutOfMemory, 'EHeapException');
  Parent(EInvalidPointer, 'EHeapException');
  Parent(EInOutError, 'Exception');
  Parent(EExternal, 'Exception');
  Parent(EExternalException, 'EExternal');
  Parent(EIntError, 'EExternal');
  Parent(EDivByZero, 'EIntError');
  Parent(ERangeError, 'EIntError');
  Parent(EIntOverflow, 'EIntError');
  Parent(EMathError, 'EExternal');
  Parent(EInvalidOp, 'EMathError');
  Parent(EZeroDivide, 'EMathError');
  Parent(EOverflow, 'EMathError');
  Parent(EUnderflow, 'EMathError');
  Parent(EAccessViolation, 'EExternal');
  Parent(EPrivilege, 'EExternal');
  Parent(EStackOverflow, 'EExternal');
  Parent(EControlC, 'EExternal');
  Parent(EInvalidCast, 'Exception');
  Parent(EConvertError, 'Exception');
  Parent(EAssertionFailed, 'Exception');
  Parent(EAbstractError, 'Exception');
  Parent(EIntfCastError, 'Exception');
  Parent(EOSError, 'Exception');
  Parent(ENotImplemented, 'Exception');
  Parent(ENotSupportedException, 'Exception');
  Parent(EInvalidOpException, 'Exception');
  Parent(EArgumentException, 'Exception');
  Parent(EArgumentOutOfRangeException, 'EArgumentException');
  Parent(EArgumentNilException, 'EArgumentException');
  Parent(EEncodingError, 'Exception');
  Parent(EFileNotFoundException, 'Exception');
  Parent(EDirectoryNotFoundException, 'Exception');
  CheckEqB(True, EZeroDivide.InheritsFrom(EExternal), 'EZeroDivide inherits EExternal');
  CheckEqB(True, ERangeError.InheritsFrom(Exception), 'ERangeError inherits Exception');
  CheckEqB(False, ERangeError.InheritsFrom(EMathError), 'ERangeError is no EMathError');
  CheckEqB(False, EConvertError.InheritsFrom(EExternal), 'EConvertError is no EExternal');
  CheckEqB(False, EOutOfMemory.InheritsFrom(EExternal), 'EOutOfMemory is no EExternal');
end;

procedure Members;
var
  E: Exception;
begin
  Section('Exception members');
  E := Exception.Create('plain');
  CheckEqS('plain', E.Message, 'Create');
  CheckEqS('plain', E.ToString, 'ToString = Message');
  CheckEqI(0, E.HelpContext, 'HelpContext 0');
  Check(E.InnerException = nil, 'no InnerException');
  Check(E.GetBaseException = E, 'GetBaseException of a single exception');
  E.Message := 'changed';
  CheckEqS('changed', E.Message, 'Message write');
  E.Free;
  E := Exception.CreateFmt('%d-%s', [7, 'x']);
  CheckEqS('7-x', E.Message, 'CreateFmt');
  E.Free;
  E := EConvertError.CreateHelp('h', 12);
  CheckEqS('h', E.Message, 'CreateHelp message');
  CheckEqI(12, E.HelpContext, 'CreateHelp context');
  E.HelpContext := 3;
  CheckEqI(3, E.HelpContext, 'HelpContext write');
  E.Free;
  E := Exception.CreateFmtHelp('%s!', ['y'], 5);
  CheckEqS('y!', E.Message, 'CreateFmtHelp message');
  CheckEqI(5, E.HelpContext, 'CreateFmtHelp context');
  E.Free;
  { ABI-125: FMessage is the first field, at instance offset 8 }
  E := Exception.Create('abi');
  Check(PPointer(NativeInt(Pointer(E)) + 8)^ = Pointer(E.Message), 'FMessage at offset 8');
  E.Free;
end;

procedure Inner;
var
  Outer: Exception;
begin
  Section('RaiseOuterException');
  Outer := nil;
  try
    try
      raise EConvertError.Create('inner');
    except
      Exception.RaiseOuterException(EArgumentException.Create('outer'));
    end;
  except
    on E: EArgumentException do
    begin
      CheckEqS('outer', E.Message, 'outer caught');
      Check(E.InnerException <> nil, 'inner attached');
      if E.InnerException <> nil then
      begin
        CheckEqS('EConvertError', E.InnerException.ClassName, 'inner class');
        CheckEqS('inner', E.InnerException.Message, 'inner message');
        Check(E.GetBaseException = E.InnerException, 'GetBaseException follows InnerException');
      end;
    end;
  end;
  try
    Exception.RaiseOuterException(EAbort.Create('alone'));
  except
    on E: EAbort do
    begin
      Check(E.InnerException = nil, 'outside a handler: no inner exception');
      Outer := E;
    end;
  end;
  Check(Outer <> nil, 'outside a handler: raised');
end;

procedure Heap;
var
  First, Second: TObject;
  P: Pointer;
begin
  Section('pre-allocated heap exceptions');
  First := nil;
  Second := nil;
  try
    GetMem(P, NativeInt($7FFFFFFFFFFF0000));
  except
    on E: EOutOfMemory do
    begin
      First := E;
      CheckEqS('Out of memory', E.Message, 'EOutOfMemory message');
    end;
  end;
  try
    GetMem(P, NativeInt($7FFFFFFFFFFF0000));
  except
    on E: EOutOfMemory do
      Second := E;
  end;
  Check((First <> nil) and (First = Second), 'the same EOutOfMemory instance both times');
  CheckEqS('Out of memory', EOutOfMemory(First).Message, 'instance kept after its handler');
end;

var
  Before: NativeInt;
begin
  Hierarchy;
  Before := _LiveAllocCount;
  Members;
  Inner;
  Heap;
  CheckEqI(0, _LiveAllocCount - Before, 'no leaks');
  Done;
end.
