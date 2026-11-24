// covers: RTL-1014
// phase: 8
program Com_DispCallProperties;
{ com-support.md RTL-1014 step 2: property get (flags 2), property put (flags 4) with exactly one named argument,
  DISPID_PROPERTYPUT, whose value is the first argument record, put of an interface value (flags 8) and an indexed
  default property. The hand-written server writes down what Invoke received and really stores the values. }
uses SysUtils, Windows, ActiveX, ComObj, TestKit;

type
  IOther = dispinterface
    property Count: Integer readonly dispid 1;
  end;

  IProps = dispinterface
    property Count: Integer readonly dispid 1;
    property Title: string dispid 2;
    property Link: IDispatch dispid 3;
    property Next: IOther dispid 4;
    property Flag: Boolean dispid 5;
    property Cell[Row, Col: Integer]: Double dispid 6;
    property Sink: Word writeonly dispid 7;
    property Item[const Key: string]: Integer dispid 0; default;
  end;

  TServer = class(TInterfacedObject, IDispatch)
  public
    Log: string;
    Title: string;
    Link: IDispatch;
    Flag: SmallInt;
    Keys: array[0..3] of string;
    Values: array[0..3] of Integer;
    Count: Integer;
    Cells: array[0..3, 0..3] of Double;
    Sink: Integer;
    function GetTypeInfoCount(out Count: Integer): HResult; stdcall;
    function GetTypeInfo(Index, LocaleID: Integer; out TypeInfo): HResult; stdcall;
    function GetIDsOfNames(const IID: TGUID; Names: Pointer; NameCount, LocaleID: Integer;
      DispIDs: Pointer): HResult; stdcall;
    function Invoke(DispID: Integer; const IID: TGUID; LocaleID: Integer; Flags: Word; var Params;
      VarResult, ExcepInfo, ArgErr: Pointer): HResult; stdcall;
    function Find(const Key: string): Integer;
  end;

function BStrToStr(B: TBStr): string;
begin
  SetString(Result, B, SysStringLen(B));
end;

function TServer.GetTypeInfoCount(out Count: Integer): HResult;
begin
  Count := 0;
  Result := S_OK;
end;

function TServer.GetTypeInfo(Index, LocaleID: Integer; out TypeInfo): HResult;
begin
  Result := E_NOTIMPL;
end;

function TServer.GetIDsOfNames(const IID: TGUID; Names: Pointer; NameCount, LocaleID: Integer;
  DispIDs: Pointer): HResult;
begin
  Result := E_NOTIMPL;
end;

{ the index of Key, added when new }
function TServer.Find(const Key: string): Integer;
begin
  Result := 0;
  while (Result < Count) and (Keys[Result] <> Key) do
    Inc(Result);
  if Result = Count then
  begin
    Keys[Count] := Key;
    Values[Count] := 0;
    Inc(Count);
  end;
end;

function TServer.Invoke(DispID: Integer; const IID: TGUID; LocaleID: Integer; Flags: Word; var Params;
  VarResult, ExcepInfo, ArgErr: Pointer): HResult;
var
  DP: PDispParams;
  A: array[0..2] of TVariantArg;
  V: PVariantArg;
  R: PVariantArg;
  I: Integer;
  Put: Boolean;
begin
  DP := @Params;
  R := VarResult;
  Log := 'id=' + IntStr(DispID) + ' flags=' + IntStr(Flags) + ' args=' + IntStr(DP^.cArgs) + ' named=' +
    IntStr(DP^.cNamedArgs);
  if DP^.cNamedArgs > 0 then
    Log := Log + ' name=' + IntStr(DP^.rgdispidNamedArgs^)
  else if DP^.rgdispidNamedArgs <> nil then
    Log := Log + ' NAMES-NOT-NIL';
  if R = nil then
    Log := Log + ' noresult'
  else
    Log := Log + ' result';
  V := DP^.rgvarg;
  for I := 0 to Integer(DP^.cArgs) - 1 do
  begin
    A[I] := V^;
    Log := Log + ' vt' + IntStr(V^.vt);
    Inc(V);
  end;
  Put := Flags and (DISPATCH_PROPERTYPUT or DISPATCH_PROPERTYPUTREF) <> 0;
  { a put carries the new value in A[0]; index arguments follow in reverse order }
  Result := S_OK;
  case DispID of
    0:
      if Put then
        Values[Find(BStrToStr(A[1].bstrVal))] := A[0].lVal
      else
      begin
        R^.vt := VT_I4;
        R^.lVal := Values[Find(BStrToStr(A[0].bstrVal))];
      end;
    1:
      begin
        R^.vt := VT_I4;
        R^.lVal := Count;
      end;
    2:
      if Put then
        Title := BStrToStr(A[0].bstrVal)
      else
      begin
        R^.vt := VT_BSTR;
        R^.bstrVal := SysAllocStringLen(PWideChar(Title), Length(Title));
      end;
    3, 4:
      if Put then
        Link := IDispatch(A[0].unkVal)
      else
      begin
        R^.vt := VT_DISPATCH;
        if Link <> nil then
          Link._AddRef;
        R^.unkVal := Pointer(Link);
      end;
    5:
      if Put then
        Flag := A[0].boolVal
      else
      begin
        R^.vt := VT_BOOL;
        R^.boolVal := Flag;
      end;
    6:
      if Put then
        Cells[A[2].lVal, A[1].lVal] := A[0].dblVal
      else
      begin
        R^.vt := VT_R8;
        R^.dblVal := Cells[A[1].lVal, A[0].lVal];
      end;
    7: Sink := A[0].lVal and $FFFF;
  else
    Result := DISP_E_MEMBERNOTFOUND;
  end;
end;

var
  Srv, OtherSrv: TServer;
  Keep, OtherRef: IDispatch;
  D: IProps;
  Before: Integer;

{ the interface properties are read here, so that every temporary reference ends with the procedure }
procedure ReadLinks;
var
  Got: IDispatch;
  O: IOther;
begin
  Got := D.Link;
  CheckEqS('id=3 flags=2 args=0 named=0 result', Srv.Log, 'get of an interface property');
  Check(Pointer(Got) = Pointer(OtherRef), 'the same object comes back');
  O := D.Next;
  Check(Pointer(O) = Pointer(OtherRef), 'a dispatch-interface property');
end;

procedure ReadNilLink;
begin
  Check(D.Link = nil, 'nil comes back');
end;

begin
  Srv := TServer.Create;
  Keep := Srv;
  OtherSrv := TServer.Create;
  OtherRef := OtherSrv;
  D := Keep;

  Section('property get: flags 2, no named argument (RTL-1014 step 2)');
  CheckEqI(0, D.Count, 'Count');
  CheckEqS('id=1 flags=2 args=0 named=0 result', Srv.Log, 'get of a plain property');
  CheckEqS('', D.Title, 'an empty string property');
  CheckEqS('id=2 flags=2 args=0 named=0 result', Srv.Log, 'get of a string property');

  Section('property put: flags 4, one named argument -3 (RTL-1014 step 2)');
  D.Title := 'new title';
  CheckEqS('id=2 flags=4 args=1 named=1 name=-3 noresult vt8', Srv.Log, 'put of a string');
  CheckEqS('new title', Srv.Title, 'the server stored it');
  CheckEqS('new title', D.Title, 'and gives it back');
  D.Flag := True;
  CheckEqS('id=5 flags=4 args=1 named=1 name=-3 noresult vt11', Srv.Log, 'put of a Boolean');
  CheckEqI(-1, Srv.Flag, 'True arrives as -1');
  CheckEqB(True, D.Flag, 'read back');
  D.Flag := False;
  CheckEqI(0, Srv.Flag, 'False arrives as 0');
  CheckEqB(False, D.Flag, 'read back');
  D.Sink := 65535;
  CheckEqS('id=7 flags=4 args=1 named=1 name=-3 noresult vt18', Srv.Log, 'put of a write-only property');
  CheckEqI(65535, Srv.Sink, 'its value');

  Section('put of an interface value: flags 8 (RTL-1014 step 2)');
  Before := OtherSrv.RefCount;
  D.Link := OtherRef;
  CheckEqS('id=3 flags=8 args=1 named=1 name=-3 noresult vt9', Srv.Log, 'put by reference of an IDispatch');
  CheckEqI(Before + 1, OtherSrv.RefCount, 'only the server took a reference');
  ReadLinks;
  CheckEqI(Before + 1, OtherSrv.RefCount, 'reading it left no reference behind');
  D.Next := nil;
  CheckEqS('id=4 flags=8 args=1 named=1 name=-3 noresult vt9', Srv.Log, 'put of nil');
  CheckEqI(Before, OtherSrv.RefCount, 'the server released the object');
  ReadNilLink;

  Section('indexed and default properties (RTL-1014 step 2)');
  D.Item['a'] := 7;
  CheckEqS('id=0 flags=4 args=2 named=1 name=-3 noresult vt3 vt8', Srv.Log,
    'indexed put: the value first, then the index');
  D['b'] := 8;
  CheckEqS('id=0 flags=4 args=2 named=1 name=-3 noresult vt3 vt8', Srv.Log, 'default property put');
  CheckEqI(7, D.Item['a'], 'indexed get');
  CheckEqS('id=0 flags=2 args=1 named=0 result vt8', Srv.Log, 'indexed get: flags 2, one argument');
  CheckEqI(8, D['b'], 'default property get');
  CheckEqI(2, D.Count, 'two keys are stored');
  D['a'] := D['a'] + D['b'];
  CheckEqI(15, D['a'], 'get and put in one statement');
  D.Cell[2, 3] := 4.5;
  CheckEqS('id=6 flags=4 args=3 named=1 name=-3 noresult vt5 vt3 vt3', Srv.Log, 'two indexes and the value');
  CheckEqF(4.5, Srv.Cells[2, 3], 0, 'stored at row 2, column 3');
  CheckEqF(4.5, D.Cell[2, 3], 0, 'read back');
  CheckEqS('id=6 flags=2 args=2 named=0 result vt3 vt3', Srv.Log, 'two indexes');
  CheckEqF(0.0, D.Cell[3, 2], 0, 'the other cell is untouched');

  D := nil;
  Srv.Link := nil;
  OtherRef := nil;
  Keep := nil;
  Done;
end.
