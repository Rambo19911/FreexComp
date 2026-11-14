// covers: LANG-644, ABI-140
program RttiStoredDefault;

{ stored and default specifiers in TPropInfo (ABI section 10.1): StoredKind 0 constant (Stored = 0/1), 1 field (Stored =
  the field offset), 2 static method (a code address), 3 virtual method (a VMT offset); Default = the ordinal or set bits,
  $8000000000000000 for none (also with nodefault). }

{$POINTERMATH ON}

type
  TKind = (kA, kB, kC);
  TKinds = set of TKind;
  TStoredFn = function(Self: Pointer): Boolean;
  PBool = ^Boolean;

  {$M+}
  TItem = class
  private
    FA, FB, FD, FE: Integer;
    FC, FFlag: Boolean;
    FK: TKind;
    FS: TKinds;
    function IsDStored: Boolean;
  protected
    function IsEStored: Boolean; virtual;
  published
    property A: Integer read FA write FA default 5;
    property B: Integer read FB write FB stored False;
    property C: Boolean read FC write FC stored FFlag;
    property D: Integer read FD write FD stored IsDStored nodefault;
    property E: Integer read FE write FE stored IsEStored;
    property K: TKind read FK write FK default kB;
    property S: TKinds read FS write FS default [kA, kC];
  end;
  {$M-}

function TItem.IsDStored: Boolean;
begin
  Result := FD > 0;
end;

function TItem.IsEStored: Boolean;
begin
  Result := True;
end;

function At(P: Pointer; Offset: Integer): Pointer;
begin
  Result := PPointer(PByte(P) + Offset)^;
end;

function Stored(Obj: TObject; P: PByte): Boolean;
begin
  case P[18] of
    0: Result := PNativeInt(P + 40)^ <> 0;
    1: Result := PBool(PByte(Obj) + PNativeInt(P + 40)^)^;
    2: Result := TStoredFn(Pointer(PNativeInt(P + 40)^))(Pointer(Obj));
  else
    Result := TStoredFn(At(PPointer(Obj)^, PNativeInt(P + 40)^))(Pointer(Obj));
  end;
end;

var
  Item: TItem;
  TI, P: PByte;
  I: Integer;
begin
  Item := TItem.Create;
  Item.FFlag := True;
  Item.FD := 0;
  TI := At(PPointer(Item)^, 24);
  for I := 0 to PNativeInt(TI + 40)^ - 1 do
  begin
    P := PByte(At(TI, 48)) + I * 64;
    if P[19] = 3 then
    begin
      Write(PChar(At(P, 0)), ' stored ', P[18], ' ', Stored(Item, P));
      if PInt64(P + 56)^ = Low(Int64) then
        Writeln(' default none')
      else
        Writeln(' default ', PInt64(P + 56)^);
    end;
  end;
  Item.Free;
end.