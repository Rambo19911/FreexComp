// covers: LANG-682
program IntfPointerCast;

{ Pointer(I) and IFoo(P) reinterpret the reference and count nothing; storing the result of IFoo(P) in a variable
  counts once, like every interface assignment. The object logs its own _AddRef/_Release. }

type
  IFoo = interface
    ['{5E0C1A11-0B6F-4C1D-9A52-3D1F6E7A8B01}']
    procedure Hello;
  end;

  TFoo = class(TObject, IFoo)
    FRef: Integer;
    function QueryInterface(const IID: TGUID; out Obj): HResult; stdcall;
    function _AddRef: Integer; stdcall;
    function _Release: Integer; stdcall;
    procedure Hello;
  end;

function TFoo.QueryInterface(const IID: TGUID; out Obj): HResult;
begin
  if GetInterface(IID, Obj) then
    Result := 0
  else
    Result := HResult($80004002);
end;

function TFoo._AddRef: Integer;
begin
  Inc(FRef);
  WriteLn('  addref -> ', FRef);
  Result := FRef;
end;

function TFoo._Release: Integer;
begin
  Dec(FRef);
  WriteLn('  release -> ', FRef);
  Result := FRef;
  if FRef = 0 then
  begin
    WriteLn('  destroyed');
    Destroy;
  end;
end;

procedure TFoo.Hello;
begin
  WriteLn('  hello ', FRef);
end;

procedure UseBorrowed(P: Pointer);
begin
  WriteLn('borrowed call');
  IFoo(P).Hello;
end;

procedure Run;
var
  A, B: IFoo;
  P, Q: Pointer;
begin
  WriteLn('create');
  A := TFoo.Create;
  WriteLn('to pointer');
  P := Pointer(A);
  WriteLn(P <> nil);
  UseBorrowed(P);
  WriteLn('cast back, no store');
  Q := Pointer(IFoo(P));
  WriteLn(P = Q);
  WriteLn('cast back, store');
  B := IFoo(P);
  B.Hello;
  WriteLn('same reference ', Pointer(B) = P);
  WriteLn('clear B');
  B := nil;
  WriteLn('nil pointer');
  P := nil;
  B := IFoo(P);
  WriteLn(B = nil);
  WriteLn('adopt an owned pointer');
  P := Pointer(A);
  IFoo(P)._AddRef;
  B := IFoo(P);
  IFoo(P)._Release;
  WriteLn('leave');
end;

begin
  Run;
  WriteLn('done');
end.
