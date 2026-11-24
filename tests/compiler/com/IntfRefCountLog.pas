// covers: LANG-683
program IntfRefCountLog;

{ Card acceptance 2: a hand-written IUnknown implementation (no TInterfacedObject) logs every _AddRef and _Release with
  the resulting count: assignment, re-assignment, value parameter, const parameter (no calls), function result and
  scope exit. }

type
  ILog = interface
    ['{5E0C1A11-0B6F-4C1D-9A52-3D1F6E7A8B60}']
    function Name: string;
  end;

  TLog = class(TObject, ILog)
    FName: string;
    FRef: Integer;
    constructor Create(const AName: string);
    function QueryInterface(const IID: TGUID; out Obj): HResult; stdcall;
    function _AddRef: Integer; stdcall;
    function _Release: Integer; stdcall;
    function Name: string;
  end;

constructor TLog.Create(const AName: string);
begin
  FName := AName;
end;

function TLog.QueryInterface(const IID: TGUID; out Obj): HResult;
begin
  if GetInterface(IID, Obj) then
    Result := 0
  else
    Result := HResult($80004002);
end;

function TLog._AddRef: Integer;
begin
  Inc(FRef);
  WriteLn('  ', FName, ' addref -> ', FRef);
  Result := FRef;
end;

function TLog._Release: Integer;
begin
  Dec(FRef);
  WriteLn('  ', FName, ' release -> ', FRef);
  Result := FRef;
  if FRef = 0 then
  begin
    WriteLn('  ', FName, ' destroyed');
    Destroy;
  end;
end;

function TLog.Name: string;
begin
  Result := FName;
end;

procedure ByValue(L: ILog);
begin
  WriteLn('  in ByValue ', L.Name);
end;

procedure ByConst(const L: ILog);
begin
  WriteLn('  in ByConst ', L.Name);
end;

function Make(const AName: string): ILog;
begin
  Result := TLog.Create(AName);
end;

procedure Run;
var
  A, B: ILog;
begin
  WriteLn('assignment');
  A := TLog.Create('one');
  WriteLn('second reference');
  B := A;
  WriteLn('re-assignment');
  B := TLog.Create('two');
  WriteLn('value parameter');
  ByValue(A);
  WriteLn('const parameter');
  ByConst(A);
  WriteLn('function result');
  B := Make('three');
  WriteLn('nil');
  A := nil;
  WriteLn('scope exit');
end;

begin
  Run;
  WriteLn('done');
end.
