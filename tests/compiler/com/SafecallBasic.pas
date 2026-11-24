// covers: LANG-686, LANG-687, LANG-688
program SafecallBasic;

{ An object with safecall methods, called (a) through its safecall interface and (b) through a twin interface with the
  same GUID and slot order whose methods are stdcall with an explicit HResult and out parameters. }

uses
  SysUtils;

type
  INamed = interface
    ['{5E0C1A11-0B6F-4C1D-9A52-3D1F6E7A8B41}']
    function Name: string;
  end;

  IService = interface
    ['{5E0C1A11-0B6F-4C1D-9A52-3D1F6E7A8B40}']
    procedure Ping; safecall;
    function Count: Integer; safecall;
    function Title(N: Integer): string; safecall;
    function Child: INamed; safecall;
    function Fail(N: Integer): string; safecall;
  end;

  IServiceRaw = interface
    ['{5E0C1A11-0B6F-4C1D-9A52-3D1F6E7A8B40}']
    function Ping: HResult; stdcall;
    function Count(out Value: Integer): HResult; stdcall;
    function Title(N: Integer; out Value: string): HResult; stdcall;
    function Child(out Value: INamed): HResult; stdcall;
    function Fail(N: Integer; out Value: string): HResult; stdcall;
  end;

  TNamed = class(TInterfacedObject, INamed)
    function Name: string;
  end;

  TService = class(TInterfacedObject, IService)
    procedure Ping; safecall;
    function Count: Integer; safecall;
    function Title(N: Integer): string; safecall;
    function Child: INamed; safecall;
    function Fail(N: Integer): string; safecall;
  end;

function TNamed.Name: string;
begin
  Result := 'child';
end;

procedure TService.Ping;
begin
  WriteLn('  ping');
end;

function TService.Count: Integer;
begin
  Result := 42;
end;

function TService.Title(N: Integer): string;
begin
  if N = 0 then
    Exit('none');
  Result := 'title ' + IntToStr(N);
end;

function TService.Child: INamed;
begin
  Result := TNamed.Create;
end;

function TService.Fail(N: Integer): string;
begin
  Result := 'partial ' + IntToStr(N);
  raise Exception.Create('boom');
end;

procedure Run;
var
  A: IService;
  B: IServiceRaw;
  N, I: Integer;
  S: string;
  C: INamed;
  HR: HResult;
  Live: NativeInt;
begin
  A := TService.Create;
  WriteLn('safecall');
  A.Ping;
  WriteLn('  ', A.Count);
  WriteLn('  ', A.Title(7));
  WriteLn('  ', A.Title(0));
  WriteLn('  ', A.Child.Name);

  WriteLn('stdcall twin');
  B := IServiceRaw(Pointer(A));
  HR := B.Ping;
  WriteLn('  ', HR);
  HR := B.Count(N);
  WriteLn('  ', HR, ' ', N);
  HR := B.Title(7, S);
  WriteLn('  ', HR, ' ', S);
  HR := B.Child(C);
  WriteLn('  ', HR, ' ', C.Name);
  C := nil;

  WriteLn('raising method');
  S := 'before';
  HR := B.Fail(1, S);
  WriteLn('  ', IntToHex(HR, 8), ' [', S, ']');
  S := 'kept';
  try
    S := A.Fail(2);
    WriteLn('  not reached');
  except
    on E: Exception do
      WriteLn('  ', E.ClassName, ' [', S, ']');
  end;

  S := '';
  Live := _LiveAllocCount;
  for I := 1 to 3 do
    S := A.Title(I);
  WriteLn('loop ', S, ' ', _LiveAllocCount - Live);
end;

begin
  Run;
  WriteLn('done');
end.
