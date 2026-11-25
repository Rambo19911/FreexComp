// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
{ RTLConsts - the message texts of the RTL (other-units.md section 6, RTL-650): the SysUtils catalogue
  (sysutils.md section 11), the Classes/TypInfo catalogue (classes.md section 12, form reader texts of RTL-500) and
  the per-unit additions of other-units.md section 6. Strings only, no code. }
unit RTLConsts;

interface

resourcestring
  { ---------------------------------------------------------------- SysUtils (sysutils.md section 11) }
  SInvalidInteger = '''%s'' is not a valid integer value';
  SInvalidFloat = '''%s'' is not a valid floating point value';
  SInvalidBoolean = '''%s'' is not a valid boolean value';
  SInvalidDate = '''%s'' is not a valid date';
  SInvalidTime = '''%s'' is not a valid time';
  SInvalidDateTime = '''%s'' is not a valid date and time';
  SInvalidGUID = '''%s'' is not a valid GUID value';
  SDateEncodeError = 'Invalid argument to date encode';
  STimeEncodeError = 'Invalid argument to time encode';
  SInvalidFormat = 'Format ''%s'' invalid or incompatible with argument';
  SArgumentMissing = 'No argument for format ''%s''';
  SDivByZero = 'Division by zero';
  SRangeError = 'Range check error';
  SIntOverflow = 'Integer overflow';
  SInvalidOp = 'Invalid floating point operation';
  SZeroDivide = 'Floating point division by zero';
  SOverflow = 'Floating point overflow';
  SUnderflow = 'Floating point underflow';
  SInvalidPointer = 'Invalid pointer operation';
  SOutOfMemory = 'Out of memory';
  SInvalidCast = 'Invalid class typecast';
  SAccessViolation = 'Access violation at address %s: %s of address %s';
  SAccessViolationError = 'Access violation';                { 216, sysutils.md RTL-212 }
  SStackOverflow = 'Stack overflow';
  SPrivilege = 'Privileged instruction';
  SExternalException = 'External exception %x';
  SAbstractError = 'Abstract method called';
  SAssertionFailed = 'Assertion failed';
  SAssertError = '%s (%s, line %d)';
  SIntfCastError = 'Interface not supported';
  SSafecallException = 'Exception in safecall method';
  SDispatchError = 'Dispatch calls need unit ComObj';
  SOleError = 'OLE error %.8x';
  SOperationAborted = 'Operation aborted';
  SOSError = 'System Error.  Code: %d.'#13#10'%s';
  SUnkOSError = 'A call to an OS function failed';
  SInOutError = 'I/O error %d';
  SRuntimeError = 'Runtime error %d';
  SFileNotFound = 'File not found';                         { I/O error 2 (RTL-212) }
  SPathNotFound = 'Path not found';                         { 3 }
  STooManyOpenFiles = 'Too many open files';                { 4 }
  SAccessDenied = 'File access denied';                     { 5 }
  SInvalidFileHandle = 'Invalid file handle';               { 6 }
  SEndOfFile = 'Read beyond end of file';                   { 100 }
  SDiskFull = 'Disk write error';                           { 101 }
  SFileNotAssigned = 'File not assigned';                   { 102 }
  SFileNotOpen = 'File not open';                           { 103 }
  SFileNotOpenForInput = 'File not open for input';         { 104 }
  SFileNotOpenForOutput = 'File not open for output';       { 105 }
  SInvalidInput = 'Invalid numeric input';                  { 106 }
  SCannotCreateDir = 'Cannot create directory';
  SIOFileNotFound = 'File "%s" not found';                 { IOUtils (RTL-690) }
  SIODirectoryNotFound = 'Directory "%s" not found';
  SUnhandledException = 'Unhandled exception %s: %s';
  SUnhandledExceptionNoMsg = 'Unhandled exception %s';
  SExceptionInModule = 'Exception %s in module %s at %s.';
  SIndexOutOfRange = 'Index out of range (%d)';
  SCapacityLessThanLength = 'Capacity cannot be less than Length';
  SInvalidBufferRange = 'Invalid buffer range';

  { ---------------------------------------------------------------- Classes / TypInfo (classes.md section 12) }
  SListIndexError = 'List index out of bounds (%d)';
  SListCapacityError = 'List capacity out of bounds (%d)';
  SListCountError = 'List count out of bounds (%d)';
  SSortedListError = 'Operation not allowed on sorted list';
  SDuplicateString = 'String list does not allow duplicates';
  SDuplicateItem = 'List does not allow duplicates ($0%x)';
  SAssignError = 'Cannot assign a %s to a %s';
  SAssignNil = 'Cannot assign nil to a %s';
  SWriteRootNotImplemented = 'TWriter.WriteRootComponent is not implemented';   { the TWriter stub, RTL-481 }
  SReadError = 'Stream read error';
  SWriteError = 'Stream write error';
  SStreamSetSize = 'Unable to change stream size';
  SStreamNoRead = '%s.Read not implemented';
  SStreamNoWrite = '%s.Write not implemented';
  SFCreateErrorEx = 'Cannot create file "%s". %s';
  SFOpenErrorEx = 'Cannot open file "%s". %s';
  SClassNotFound = 'Class %s not found';
  SDuplicateClass = 'A class named %s already exists';
  SDuplicateName = 'A component named %s already exists';
  SInvalidName = '"%s" is not a valid component name';
  SResNotFound = 'Resource %s not found';
  SUnknownProperty = 'Property "%s" does not exist';
  SPropertyWrongType = 'Property "%s" is of the wrong type';
  SReadOnlyProperty = 'Property "%s" is read-only';
  SWriteOnlyProperty = 'Property "%s" is write-only';
  SInvalidPropertyValue = 'Invalid property value';
  SThreadAlreadyStarted = 'Thread already started';
  SCheckSynchronizeError = 'CheckSynchronize called from thread $%x, which is NOT the main thread';
  { form reader errors (RTL-500); the reader prefixes '<SourceName>(<line>,<col>): error <code>: ' }
  SFormSyntaxError = 'Syntax error: %s';                    { F0001 }
  SFormClassNotFound = 'Class %s not found';                { F0101 }
  SFormPropertyNotFound = 'Property %s does not exist on %s'; { F0102 }
  SFormInvalidPropertyValue = 'Invalid value for property %s'; { F0103 }
  SFormMethodNotFound = 'Method %s not found in %s';        { F0104 }
  SFormComponentNotFound = 'Component %s not found';        { F0105 }
  SFormDuplicateName = 'A component named %s already exists'; { F0106 }
  SFormObjectsNotSupported = '"%s" objects are not supported'; { F0107 }

  { ---------------------------------------------------------------- other units (other-units.md section 6) }
  SInvalidArgument = 'Invalid argument';
  SArgumentOutOfRange = 'Argument out of range';
  SArgumentNil = 'Argument must not be nil';
  SGenericItemNotFound = 'Item not found';
  SGenericDuplicateItem = 'Duplicates not allowed';
  SUnbalancedOperation = 'Unbalanced stack or queue operation';
  SNoDefaultComparer = 'No default comparer for type %s';    { RTL-671 }
  SIniFileWriteError = 'Unable to write to %s';
  SLockNotOwned = 'Lock is not owned by the calling thread';
  SWaitFailed = 'Wait failed: %s';

implementation

end.
