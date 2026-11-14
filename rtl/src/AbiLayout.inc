{ The private records through which System reads the layouts shared with the compiler (RTL-029). This file is the
  only declaration: System includes it in its implementation and the test Abi_LayoutRecords includes it to assert
  sizes and field offsets against rtl-abi.md. TVmtHeader (classes, R-059) and TVarRec (variant records, M2.5) are
  added with the tasks that need them. }
type
  { rtl-abi.md section 3.1: in front of the first element of a long string }
  TStrRec = record
    RefCount: Integer;                 { -16 }
    Length: Integer;                   { -12 }
    CodePage: Word;                    { -8 }
    ElemSize: Word;                    { -6 }
    Reserved: Integer;                 { -4 }
  end;
  PStrRec = ^TStrRec;

  { rtl-abi.md section 4: in front of element 0 of a dynamic array }
  TDynArrayRec = record
    RefCount: Integer;                 { -16 }
    Reserved: Integer;                 { -12 }
    Length: NativeInt;                 { -8 }
  end;
  PDynArrayRec = ^TDynArrayRec;

  { rtl-abi.md section 10: the type-info blocks read by the managed-value helpers (common header: Kind, 7 zero bytes,
    Name); the compiler emits them as anonymous structs with natural alignment (C-064) }
  TDynArrayTypeInfo = record           { tkDynArray }
    Kind: Byte;                        { 0 }
    Pad: array[0..6] of Byte;          { 1 }
    Name: Pointer;                     { 8 }
    ElSize: NativeInt;                 { 16 }
    ElType: Pointer;                   { 24: null for element kinds without a block }
    ManagedElType: Pointer;            { 32: null when the elements are not managed }
  end;
  PDynArrayTypeInfo = ^TDynArrayTypeInfo;

  TRecordTypeInfo = record             { tkRecord }
    Kind: Byte;
    Pad: array[0..6] of Byte;
    Name: Pointer;
    Size: NativeInt;                   { 16 }
    ManagedCount: NativeInt;           { 24 }
    ManagedFields: Pointer;            { 32: ManagedCount x TManagedField }
  end;
  PRecordTypeInfo = ^TRecordTypeInfo;

  TManagedField = record
    TypeInfo: Pointer;                 { 0 }
    Offset: NativeInt;                 { 8 }
  end;
  PManagedField = ^TManagedField;

  TArrayTypeInfo = record              { tkArray (static arrays) }
    Kind: Byte;
    Pad: array[0..6] of Byte;
    Name: Pointer;
    Size: NativeInt;                   { 16 }
    ElCount: NativeInt;                { 24: all dimensions }
    ElType: Pointer;                   { 32: the innermost element type }
  end;
  PArrayTypeInfo = ^TArrayTypeInfo;

  { system.md RTL-127: a Text variable, 624 bytes (rtl-abi.md section 2) }
  TTextMode = (tmClosed, tmInput, tmOutput);
  TTextKind = (skConsole, skBytes, skAbsent);   { stream kind (RTL-090); "tk" is taken by TTypeKind }
  TTextRec = record
    Handle: THandle;                   { 0 }
    Buffer: PByte;                     { 8: TextBufSize bytes from _GetMem }
    Mode: TTextMode;                   { 16 }
    Kind: TTextKind;                   { 17 }
    CodePage: Word;                    { 18 }
    BufPos: Integer;                   { 20: bytes used (console kind: 2 per UTF-16 unit) }
    BufEnd: Integer;                   { 24: input only }
    PendingHigh: WideChar;             { 28: high surrogate waiting for its low half (RTL-093) }
    EofSeen: Boolean;                  { 30 }
    Name: array[0..259] of WideChar;   { 32: empty for the standard streams }
    Padding: array[0..71] of Byte;     { 552 ... 623 }
  end;
  PTextRec = ^TTextRec;

const
  TextBufSize = 8192;
