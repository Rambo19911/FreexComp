// covers: RTL-1004, CMP-1208
program DispCallNoSupportUnit;

{ A dispatch call in a program without unit ComObj: nothing installed System.DispCallProc, so the call is run-time error 231. }

type
  IProbe = dispinterface
    procedure Ping(N: Integer); dispid 1;
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
  WriteLn('Invoke must not be reached');
  Result := 0;
end;

var
  Target: IDispatch;
  D: IProbe;

begin
  Target := TDummy.Create;
  D := Target;
  WriteLn('before');
  D.Ping(1);
  WriteLn('after');
end.
