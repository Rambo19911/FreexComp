// covers: RTL-020, RTL-070, RTL-071
program Sys_StrRefCount;
{ Reference counts of UnicodeString values (rtl-abi.md section 3): literals are static blocks with count -1 that are
  never counted or freed; heap blocks start at 1, every owner adds one, scope exit and reassignment release; the
  last release frees the block (the live allocation count returns to its baseline). }
var
  A, B, C: string;
  Base: NativeInt;
  I: Integer;
  Ch: Char;

function Raw(const S: string): Pointer;
begin
  Result := PPointer(@S)^;
end;

procedure Local;
var
  L: string;
begin
  L := A;
  Writeln('local owner ', StringRefCount(A));
end;

procedure ByValue(S: string);
begin
  Writeln('value parameter ', StringRefCount(S));
end;

procedure ByConst(const S: string);
begin
  Writeln('const parameter ', StringRefCount(S));
end;

procedure ByVar(var S: string);
begin
  Writeln('var parameter ', StringRefCount(S));
  S := 'replaced';
end;

function Make: string;
begin
  Result := A;
end;

procedure ViaResult;
var
  L: string;
begin
  L := Make;
  L := Make;
end;

begin
  Base := _LiveAllocCount;
  Writeln('empty ', StringRefCount(C), ' ', StringCodePage(C), ' ', StringElementSize(C), ' nil ', Raw(C) = nil);

  A := 'literal';
  B := A;
  Writeln('literal ', StringRefCount(A), ' ', StringRefCount(B), ' same block ', Raw(A) = Raw(B));
  Writeln('literal blocks allocate nothing ', _LiveAllocCount - Base);

  A := A + '!';
  Writeln('heap ', A, ' ', StringRefCount(A), ' ', StringCodePage(A), ' ', StringElementSize(A));
  Writeln('one block ', _LiveAllocCount - Base);
  B := A;
  Writeln('shared ', StringRefCount(A), ' same block ', Raw(A) = Raw(B));
  C := B;
  Writeln('three owners ', StringRefCount(A));
  Local;
  Writeln('after local ', StringRefCount(A));
  ByValue(A);
  Writeln('after value ', StringRefCount(A));
  ByConst(A);
  ViaResult;
  Writeln('after result ', StringRefCount(A));
  A := A;
  Writeln('self assignment ', StringRefCount(A), ' ', A);
  for I := 1 to 1000 do
    C := B;
  Writeln('after 1000 assignments ', StringRefCount(A));
  B := '';
  Writeln('released one ', StringRefCount(A), ' nil ', Raw(B) = nil);
  C := 'other';
  Writeln('released two ', StringRefCount(A));
  ByVar(A);
  Writeln('var replaced ', A, ' ', StringRefCount(A));
  Writeln('freed ', _LiveAllocCount - Base);

  Ch := #$263A;
  C := Ch;
  Writeln('from char ', StringRefCount(C), ' ', Length(C), ' ', Ord(C[1]));
  B := C;
  C := '';
  Writeln('moved ', StringRefCount(B));
  B := '';
  A := '';
  Writeln('all released ', _LiveAllocCount - Base);
end.
