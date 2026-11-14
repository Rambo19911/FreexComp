// covers: LANG-640, ABI-144, CMP-408, LANG-481
program RttiPublishedMethods;

{ Published methods found by name (case-insensitive) with TObject.MethodAddress, which walks the TMethodInfo tables
  (class RTTI +56/+64) of the class and its ancestors and considers only Visibility 3; the code is called through a
  TMethod built from it. The descendant's entry for an overridden method holds the descendant's implementation.
  MethodName maps a code address back to its name. A public method is never found. }

{$POINTERMATH ON}

type
  TNotify = procedure(Sender: TObject) of object;
  TCalc = function(X: Integer): Integer of object;

  TBase = class
  published
    procedure Click(Sender: TObject);
    function Twice(X: Integer): Integer; virtual;
  public
    procedure NotPublished(Sender: TObject);
  end;

  TChild = class(TBase)
  published
    function Twice(X: Integer): Integer; override;
    procedure Extra(Sender: TObject);
    class function Info: Integer;
  end;

procedure TBase.Click(Sender: TObject);
begin
  Writeln('Click on ', ClassName, ' sender nil ', Sender = nil);
end;

function TBase.Twice(X: Integer): Integer;
begin
  Result := X * 2;
end;

procedure TBase.NotPublished(Sender: TObject);
begin
end;

function TChild.Twice(X: Integer): Integer;
begin
  Result := X * 2 + 1;
end;

procedure TChild.Extra(Sender: TObject);
begin
  Writeln('Extra');
end;

class function TChild.Info: Integer;
begin
  Result := 7;
end;

function At(P: Pointer; Offset: Integer): Pointer;
begin
  Result := PPointer(PByte(P) + Offset)^;
end;

var
  B: TBase;
  C: TChild;
  M: TMethod;
  N: TNotify;
  F: TCalc;
  TI, E: PByte;
  I: Integer;
begin
  B := TBase.Create;
  C := TChild.Create;

  M.Code := C.MethodAddress('Click');
  M.Data := Pointer(C);
  N := TNotify(M);
  N(nil);
  M.Code := C.MethodAddress('TWICE');
  F := TCalc(M);
  Writeln('child Twice ', F(20));
  M.Code := B.MethodAddress('twice');
  M.Data := Pointer(B);
  F := TCalc(M);
  Writeln('base Twice ', F(20));
  M.Code := C.MethodAddress('Extra');
  M.Data := Pointer(C);
  N := TNotify(M);
  N(C);
  Writeln('name ', C.MethodName(C.MethodAddress('extra')), ' ', TChild.MethodName(M.Code));
  Writeln('base has Extra ', B.MethodAddress('Extra') <> nil);
  Writeln('public found ', C.MethodAddress('NotPublished') <> nil, ' missing found ', C.MethodAddress('Missing') <> nil);

  TI := TypeInfo(TChild);
  for I := 0 to PNativeInt(TI + 56)^ - 1 do
  begin
    E := PByte(At(TI, 64)) + I * 24;
    if E[16] = 3 then
      Writeln('method ', PChar(At(E, 0)), ' kind ', E[17]);
  end;
  C.Free;
  B.Free;
end.
