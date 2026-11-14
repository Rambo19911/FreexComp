// covers: RTL-070, RTL-085, ABI-030, ABI-110, ABI-140
program Sys_Managed;
{$POINTERMATH ON}
{ _Finalize, _FinalizeArray, _AddRef, _AddRefArray, _CopyRecord and _CopyArray on values described by type-info
  blocks laid out as rtl-abi.md section 10 (built here, through System's own layout records, while the compiler does
  not yet accept records with managed fields): strings, byte strings, dynamic arrays, static arrays and records
  nesting them, nil and literal parts, records as dynamic-array elements (_DynArraySetLen / _DynArrayCopy with a
  record element type); nothing leaks. }
{$I ..\..\src\AbiLayout.inc}

type
  TStrTypeInfo = record                { tkUString / tkAString }
    Kind: Byte;
    Pad: array[0..6] of Byte;
    Name: Pointer;
    CodePage: Word;
  end;

  { the record RecTI describes: Integer, string, AnsiString, array of string, array[0..1] of string }
  TValue = record
    N: Integer;                        { 0 }
    S: Pointer;                        { 8 }
    A: Pointer;                        { 16 }
    D: Pointer;                        { 24 }
    T: array[0..1] of Pointer;         { 32 }
  end;
  PValue = ^TValue;

  TStrs = array of string;

var
  StrTI, AStrTI: TStrTypeInfo;
  DynTI, RecDynTI: TDynArrayTypeInfo;
  PairTI, ValuesTI: TArrayTypeInfo;
  RecTI: TRecordTypeInfo;
  Fields: array[0..3] of TManagedField;
  Base: NativeInt;

procedure SetUp;
begin
  StrTI.Kind := 9;
  StrTI.CodePage := 1200;
  AStrTI.Kind := 10;
  AStrTI.CodePage := 65001;
  DynTI.Kind := 14;
  DynTI.ElSize := SizeOf(Pointer);
  DynTI.ElType := @StrTI;
  DynTI.ManagedElType := @StrTI;
  PairTI.Kind := 13;
  PairTI.Size := 2 * SizeOf(Pointer);
  PairTI.ElCount := 2;
  PairTI.ElType := @StrTI;
  Fields[0].TypeInfo := @StrTI;
  Fields[0].Offset := 8;
  Fields[1].TypeInfo := @AStrTI;
  Fields[1].Offset := 16;
  Fields[2].TypeInfo := @DynTI;
  Fields[2].Offset := 24;
  Fields[3].TypeInfo := @PairTI;
  Fields[3].Offset := 32;
  RecTI.Kind := 12;
  RecTI.Size := SizeOf(TValue);
  RecTI.ManagedCount := 4;
  RecTI.ManagedFields := @Fields;
  ValuesTI.Kind := 13;
  ValuesTI.Size := 2 * SizeOf(TValue);
  ValuesTI.ElCount := 2;
  ValuesTI.ElType := @RecTI;
  RecDynTI.Kind := 14;
  RecDynTI.ElSize := SizeOf(TValue);
  RecDynTI.ElType := @RecTI;
  RecDynTI.ManagedElType := @RecTI;
end;

{ the RefCount of a block, 0 for nil }
function RC(P: Pointer): Integer;
begin
  Result := 0;
  if P <> nil then
    Result := PInteger(PByte(P) - 16)^;
end;

procedure Run;
var
  X, Y, L: string;
  Z: AnsiString;
  Arr: TStrs;
  V, W: TValue;
  Two, Other: array[0..1] of TValue;
  Ptrs, Dup: array[0..2] of Pointer;
  Vs: Pointer;
  Dims: array[0..0] of NativeInt;
  I: Integer;

  procedure Counts(const Title: string);
  begin
    Writeln(Title, ': ', StringRefCount(X), ' ', StringRefCount(Y), ' ', RC(Pointer(Z)), ' ', RC(PPointer(@Arr)^));
  end;

begin
  X := 'x';
  X := X + '1';
  Y := 'y';
  Y := Y + '2';
  Z := AnsiString(X);
  SetLength(Arr, 2);
  Arr[1] := Y;
  Counts('start');

  { a value holding a reference in every managed field (V.T[1] stays nil) }
  FillChar(V, SizeOf(V), 0);
  V.N := 42;
  _UStrAsg(V.S, Pointer(X));
  _AStrAsg(V.A, Pointer(Z));
  _DynArrayAsg(V.D, PPointer(@Arr)^, @DynTI);
  _UStrAsg(V.T[0], Pointer(Y));
  Counts('V');

  { raw copy + _AddRef, then _Finalize: every part counted once more, then released and nil }
  W := V;
  _AddRef(@W, @RecTI);
  Counts('addref');
  _Finalize(@W, @RecTI);
  Counts('finalize');
  Writeln(W.N, ' ', W.S = nil, ' ', W.A = nil, ' ', W.D = nil, ' ', W.T[0] = nil, ' ', W.T[1] = nil);

  { _CopyRecord: the bytes and one more reference per part; the old destination parts are released }
  _CopyRecord(@W, @V, @RecTI);
  Counts('copy');
  Writeln(W.N, ' ', W.S = Pointer(X), ' ', W.A = Pointer(Z), ' ', W.D = V.D, ' ', W.T[0] = Pointer(Y));
  _CopyRecord(@W, @W, @RecTI);
  Counts('self');
  FillChar(Two, SizeOf(Two), 0);
  _UStrAsg(Two[0].S, Pointer(Y));
  Two[0].N := 7;
  _CopyRecord(@W, @Two[0], @RecTI);
  Counts('over');
  Writeln(W.N, ' ', W.S = Pointer(Y), ' ', W.A = nil, ' ', W.D = nil);
  _Finalize(@W, @RecTI);
  _Finalize(@Two[0], @RecTI);
  Counts('cleared');

  { arrays of records: _CopyArray / _AddRefArray / _FinalizeArray, and a static array of records }
  FillChar(Two, SizeOf(Two), 0);
  FillChar(Other, SizeOf(Other), 0);
  _CopyRecord(@Two[0], @V, @RecTI);
  _CopyRecord(@Two[1], @V, @RecTI);
  Counts('two');
  _CopyArray(@Other, @Two, @RecTI, 2);
  Counts('copyarray');
  _FinalizeArray(@Other, @RecTI, 2);
  Counts('finarray');
  Other := Two;
  _AddRefArray(@Other, @RecTI, 2);
  Counts('addrefarray');
  _Finalize(@Other, @ValuesTI);
  _CopyArray(@Other, @Two, @ValuesTI, 1);
  Counts('static');
  _FinalizeArray(@Other, @ValuesTI, 1);
  _FinalizeArray(@Two, @RecTI, 2);
  Counts('released');

  { plain string slots, literals and nil }
  Ptrs[0] := Pointer(X);
  L := 'literal';
  Ptrs[1] := Pointer(L);
  Ptrs[2] := nil;
  _AddRefArray(@Ptrs, @StrTI, 3);
  Writeln(StringRefCount(X), ' ', RC(Ptrs[1]));
  FillChar(Dup, SizeOf(Dup), 0);
  _CopyArray(@Dup, @Ptrs, @StrTI, 3);
  Writeln(StringRefCount(X), ' ', Dup[1] = Ptrs[1], ' ', Dup[2] = nil);
  _FinalizeArray(@Dup, @StrTI, 3);
  _FinalizeArray(@Ptrs, @StrTI, 3);
  Writeln(StringRefCount(X), ' ', Ptrs[0] = nil, ' ', Ptrs[1] = nil);
  _Finalize(@V.T, @PairTI);
  Counts('pair');

  { a dynamic array of records: new elements zero, a shared block copied with references, shrinking finalises }
  Vs := nil;
  Dims[0] := 3;
  _DynArraySetLen(Vs, @RecDynTI, 1, @Dims);
  Writeln(PValue(Vs)[2].N, ' ', PValue(Vs)[2].S = nil, ' ', PValue(Vs)[2].T[1] = nil);
  _CopyRecord(@PValue(Vs)[1], @V, @RecTI);
  _CopyRecord(@PValue(Vs)[2], @V, @RecTI);
  Counts('elements');
  W.D := nil;
  _DynArrayAsg(W.D, Vs, @RecDynTI);
  Dims[0] := 2;
  _DynArraySetLen(Vs, @RecDynTI, 1, @Dims);
  Counts('shared');
  _DynArrayClr(W.D, @RecDynTI);
  Counts('unshared');
  _DynArrayCopy(W.D, Vs, @RecDynTI, 1, 5);
  Writeln(PValue(W.D)[0].N, ' ', PValue(W.D)[0].S = Pointer(X));
  Counts('dcopy');
  _DynArrayClr(W.D, @RecDynTI);
  Dims[0] := 1;
  _DynArraySetLen(Vs, @RecDynTI, 1, @Dims);
  Counts('shrunk');
  _DynArrayClr(Vs, @RecDynTI);
  _Finalize(@V, @RecTI);
  Counts('end');
  I := V.N;
  Writeln(I);
end;

begin
  SetUp;
  Base := _LiveAllocCount;
  Run;
  Writeln('leaks ', _LiveAllocCount - Base);
end.
