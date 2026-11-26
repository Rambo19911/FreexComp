// covers: LANG-694, CMP-1208
program DispIdAutoNumber;

{$POINTERMATH ON}

{ C-352: a member of a dispatch interface without a dispid is numbered automatically, in declaration order, after the
  highest dispid written in that interface (1 when none is written; a negative one does not lower the start). The
  program installs its own System.DispCallProc, as DispCallHookProbe does, and prints the dispid and flags of every call. }

type
  IAuto = dispinterface
    procedure First;
    procedure Fixed; dispid 7;
    property Value: Integer;
    function Neg: Integer; dispid -4;
    property Item[I: Integer]: Integer dispid 0; default;
    procedure Last;
  end;

  INone = dispinterface
    procedure A;
    property B: Integer readonly;
    procedure C;
  end;

  INeg = dispinterface
    function Items: IDispatch; dispid -4;
    procedure After;
  end;

  TDummy = class(TInterfacedObject, IDispatch)
    function GetTypeInfoCount(out Count: Integer): HResult; stdcall;
    function GetTypeInfo(Index, LocaleID: Integer; out TypeInfo): HResult; stdcall;
    function GetIDsOfNames(const IID: TGUID; Names: Pointer; NameCount, LocaleID: Integer; DispIDs: Pointer): HResult; stdcall;
    function Invoke(DispID: Integer; const IID: TGUID; LocaleID: Integer; Flags: Word; var Params;
      VarResult, ExcepInfo, ArgErr: Pointer): HResult; stdcall;
  end;

function TDummy.GetTypeInfoCount(out Count: Integer): HResult;
begin
  Count := 0;
  Result := 0;
end;

function TDummy.GetTypeInfo(Index, LocaleID: Integer; out TypeInfo): HResult;
begin
  Result := HResult($80004001);
end;

function TDummy.GetIDsOfNames(const IID: TGUID; Names: Pointer; NameCount, LocaleID: Integer; DispIDs: Pointer): HResult;
begin
  Result := HResult($80004001);
end;

function TDummy.Invoke(DispID: Integer; const IID: TGUID; LocaleID: Integer; Flags: Word; var Params;
  VarResult, ExcepInfo, ArgErr: Pointer): HResult;
begin
  Result := HResult($80004001);
end;

procedure Probe(Dest: Pointer; const Disp: IDispatch; Desc: Pointer; Args: Pointer);
var
  P: PByte;
begin
  P := Desc;
  WriteLn('dispid ', PInteger(P)^, ' flags ', PWord(P + 4)^);
  if Dest <> nil then
    case PWord(P + 6)^ of
      3: PInteger(Dest)^ := 0;
      9: IDispatch(Dest^) := nil;
    end;
end;

procedure Run;
var
  Target: IDispatch;
  Auto: IAuto;
  None: INone;
  Neg: INeg;
  N: Integer;
begin
  Target := TDummy.Create;
  Auto := Target;
  None := Target;
  Neg := Target;

  WriteLn('-- IAuto');
  Auto.First;
  Auto.Fixed;
  N := Auto.Value;
  Auto.Value := N;
  N := N + Auto.Neg;
  N := N + Auto[1];
  Auto.Last;

  WriteLn('-- INone');
  None.A;
  N := N + None.B;
  None.C;

  WriteLn('-- INeg');
  Target := Neg.Items;
  Neg.After;
  WriteLn('sum ', N);
end;

begin
  DispCallProc := Probe;
  Run;
  WriteLn('done');
end.
