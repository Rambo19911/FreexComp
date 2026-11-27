// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
{ The private records through which System reads the layouts shared with the compiler (RTL-029). This file is the
  only declaration: System includes it in its implementation and the test Abi_LayoutRecords includes it to assert
  sizes and field offsets against rtl-abi.md. TVarRec is public (System interface, RTL-013); the same test checks
  it. }
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

  { rtl-abi.md section 4: in front of element 0 of a dynamic array. RefCount -1 = static block (ABI-045); Capacity:
    0 = exactly Length elements, > 0 = room for Capacity elements (ABI-046) }
  TDynArrayRec = record
    RefCount: Integer;                 { -16 }
    Capacity: Integer;                 { -12 }
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

  { rtl-abi.md section 7.2: the fixed part of a VMT; a class reference (TClass) is its address. InitTable, when not
    nil, is a NativeInt Count followed by Count TManagedField entries (the managed fields the class itself declares);
    virtual slot k follows at VmtFirstSlot + 8 * k }
  TVmtHeader = record
    Parent: Pointer;                   { 0: the parent VMT, nil for TObject }
    InstanceSize: NativeInt;           { 8 }
    ClassName: Pointer;                { 16: static UnicodeString payload }
    TypeInfo: Pointer;                 { 24 }
    IntfTable: Pointer;                { 32 }
    MsgTable: Pointer;                 { 40 }
    InitTable: Pointer;                { 48 }
    Reserved: Pointer;                 { 56 }
  end;
  PVmtHeader = ^TVmtHeader;

  { ABI-095: IntfTable, when not nil, is a NativeInt Count followed by Count of these (32 bytes each) }
  TIntfEntry = record
    Guid: TGUID;                       { 0 }
    Imt: Pointer;                      { 16 }
    Offset: NativeInt;                 { 24: of the hidden field in the instance }
  end;
  PIntfEntry = ^TIntfEntry;

  { system.md RTL-127: a Text variable, 624 bytes (rtl-abi.md section 2) }
  TTextMode = (tmClosed, tmInput, tmOutput);
  TTextKind = (skConsole, skBytes, skAbsent);   { stream kind (RTL-090); "tk" is taken by TTypeKind }
  TTextRec = record
    Handle: THandle;                   { 0 }
    Buffer: PByte;                     { 8: TextBufSize bytes from _GetMem }
    Mode: TTextMode;                   { 16 }
    Kind: TTextKind;                   { 17 }
    CodePage: Word;                    { 18 }
    BufPos: Integer;                   { 20: output: bytes used; input: next unread byte (console kind and code page
                                         1200: 2 bytes per UTF-16 unit) }
    BufEnd: Integer;                   { 24: input only: bytes valid in Buffer }
    PendingHigh: WideChar;             { 28: high surrogate waiting for its low half (RTL-093) }
    EofSeen: Boolean;                  { 30: input: the stream reported its end }
    Name: array[0..259] of WideChar;   { 32: empty for the standard streams; a name of 260 units or more is in
                                         LongName instead }
    Magic: Cardinal;                   { 552: TextMagic once assigned (AssignFile or start-up); else unassigned (102) }
    Look: array[0..1] of WideChar;     { 556: input: decoded UTF-16 units not yet consumed }
    LookCount: Byte;                   { 560: 0 ... 2 }
    StartDone: Boolean;                { 561: input: the first character was decoded (a leading BOM is skipped) }
    Std: Boolean;                      { 562: the handle is a standard handle (never closed by CloseFile) }
    LineStart: Boolean;                { 563: console input: the next ReadConsoleW starts a line (Ctrl+Z test) }
    NameTooLong: Boolean;              { 564: the assigned name did not fit into Name: it is LongName^ }
    Reserved: array[0..2] of Byte;     { 565 ... 567 }
    LongName: PWideChar;               { 568: a _GetMem block, freed by the next AssignFile; nil otherwise }
    Padding: array[0..47] of Byte;     { 576 ... 623 }
  end;
  PTextRec = ^TTextRec;

const
  TextBufSize = 8192;
  TextMagic = $54584654;               { 'TFXT' }
  VmtFirstSlot = 64;                   { offset of virtual slot 0 in a VMT }
