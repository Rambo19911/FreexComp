// covers: LANG-290, C-261
program CastOrdinalRValueToClass;

type
  TR = record
    lParam: NativeInt;
  end;
  TFoo = class
    Tag: Integer;
  end;
  TFooClass = class of TFoo;

function Get(V: NativeInt): TR;
begin
  Result.lParam := V;
end;

function Ident(V: NativeInt): NativeInt;
begin
  Result := V;
end;

function Small(V: Integer): Integer;
begin
  Result := V;
end;

function Wide(V: Cardinal): Cardinal;
begin
  Result := V;
end;

var
  O: TFoo;
  C: TFooClass;
  P: Pointer;
begin
  O := TFoo.Create;
  O.Tag := 42;
  WriteLn(TFoo(Get(NativeInt(O)).lParam) = O);
  WriteLn(TFoo(Ident(NativeInt(O))).Tag);
  WriteLn(TFoo(NativeInt(O)).Tag);
  WriteLn(TObject(NativeInt(O) + 0) = O);
  WriteLn(TFoo(Small(0)) = nil);
  C := TFooClass(Ident(NativeInt(TFoo)));
  WriteLn(C.ClassName);
  WriteLn(TFooClass(Get(NativeInt(TFoo)).lParam) = TFoo);
  P := Pointer(Ident(NativeInt(O)));
  WriteLn(P = Pointer(O));
  WriteLn(TFoo(Pointer(Get(NativeInt(O)).lParam)).Tag);
  WriteLn(NativeInt(Pointer(Small(-1))));
  WriteLn(NativeUInt(Pointer(Wide($FFFFFFFF))));
  O.Free;
end.
