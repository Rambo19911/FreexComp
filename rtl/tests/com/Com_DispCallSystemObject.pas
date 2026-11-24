// covers: RTL-1014
// phase: 8
program Com_DispCallSystemObject;
{ com-support.md RTL-1014 against foreign code (LANG-697): the dictionary object of the scripting run-time, which
  ships with Windows, is created in process as an IDispatch and used through a dispatch interface declared here.
  The member numbers of the declaration are first compared with what the object itself answers for the names. }
uses SysUtils, Windows, ActiveX, ComObj, TestKit;

const
  COINIT_APARTMENTTHREADED = 2;
  CLSCTX_INPROC_SERVER = 1;
  LOCALE_USER_DEFAULT = $0400;

type
  IDictionary = dispinterface
    procedure Add(const Key: string; Item: Integer); dispid 1;
    property Count: Integer readonly dispid 2;
    function Exists(const Key: string): Boolean; dispid 3;
    property Item[const Key: string]: Integer dispid 0; default;
  end;

{ the member number the object answers for Name; DISPID_UNKNOWN when it has none }
function IdOf(const Disp: IDispatch; const Name: string): Integer;
var
  Names: array[0..0] of PWideChar;
  Zero: TGUID;
begin
  FillChar(Zero, SizeOf(Zero), 0);
  Names[0] := PWideChar(Name);
  Result := DISPID_UNKNOWN;
  if Disp.GetIDsOfNames(Zero, @Names, 1, LOCALE_USER_DEFAULT, @Result) < 0 then
    Result := DISPID_UNKNOWN;
end;

procedure Run;
var
  ClassId: TGUID;
  Disp: IDispatch;
  D: IDictionary;
  Outcome: string;
begin
  Section('the system object (RTL-1015: the program initialises COM itself)');
  CheckEqI(S_OK, CLSIDFromProgID('Scripting.Dictionary', ClassId), 'the class is registered');
  Disp := nil;
  CheckEqI(S_OK, CoCreateInstance(ClassId, nil, CLSCTX_INPROC_SERVER, IDispatch, Disp), 'created in process');
  Check(Disp <> nil, 'an IDispatch reference');

  Section('the declaration matches the object');
  WriteLn('Add=', IdOf(Disp, 'Add'), ' Count=', IdOf(Disp, 'Count'), ' Exists=', IdOf(Disp, 'Exists'), ' Item=',
    IdOf(Disp, 'Item'));
  CheckEqI(1, IdOf(Disp, 'Add'), 'Add');
  CheckEqI(2, IdOf(Disp, 'Count'), 'Count');
  CheckEqI(3, IdOf(Disp, 'Exists'), 'Exists');
  CheckEqI(0, IdOf(Disp, 'Item'), 'Item');

  Section('calls through the dispatch interface (LANG-697, RTL-1014)');
  D := Disp;
  CheckEqI(0, D.Count, 'a new dictionary is empty');
  D.Add('a', 1);
  D.Add('b', 2);
  CheckEqI(2, D.Count, 'Count after two Add');
  CheckEqB(True, D.Exists('b'), 'Exists of a key');
  CheckEqB(False, D.Exists('zz'), 'Exists of a missing key');
  CheckEqI(1, D.Item['a'], 'Item get');
  CheckEqI(2, D['b'], 'the default property');
  D.Item['a'] := 5;
  CheckEqI(5, D.Item['a'], 'Item put');
  CheckEqI(2, D.Count, 'the put replaced, it did not add');

  Section('a failure of the object (RTL-1014 step 4)');
  Outcome := 'no exception';
  try
    D.Add('a', 9);
  except
    on E: EOleException do
    begin
      Outcome := E.ClassName + ' ' + IntToHex(E.ErrorCode, 8);
      Check(E.Message <> '', 'the description of the object');
    end;
    on E: Exception do
      Outcome := E.ClassName;
  end;
  CheckEqS('EOleException 800A01C9', Outcome, 'adding a key twice: the code of the object');
  CheckEqI(5, D.Item['a'], 'the value is untouched');
  CheckEqI(2, D.Count, 'and so is the count');
  D := nil;
  Disp := nil;
end;

begin
  CheckEqI(S_OK, CoInitializeEx(nil, COINIT_APARTMENTTHREADED), 'CoInitializeEx');
  Run;
  CoUninitialize;
  Done;
end.
