// covers: LANG-693, LANG-694, LANG-695, LANG-696, LANG-697
program ErrDispInterface;

type
  IPlain = interface
    ['{5E0C1A11-0B6F-4C1D-9A52-3D1F6E7A8D01}']
    procedure P;
  end;

  IBad = dispinterface(IPlain)
    ['{5E0C1A11-0B6F-4C1D-9A52-3D1F6E7A8D02}']
    procedure NoNumber;
    procedure First; dispid 1;
    procedure Second; dispid 1;
    procedure Overloaded(A: Integer); dispid 2; overload;
    procedure Convention; dispid 3; stdcall;
    function GetValue: Integer; dispid 4;
    property Value: Integer read GetValue dispid 5;
    procedure Chars(C: Char); dispid 6;
    function Records: TGUID; dispid 7;
    property Wide: AnsiString dispid 8;
    procedure Open(const A: array of Integer); dispid 9;
    procedure WithDefault(A: Integer = 1); dispid 10;
  end;

  IGood = dispinterface
    ['{5E0C1A11-0B6F-4C1D-9A52-3D1F6E7A8D03}']
    procedure Run(A: Integer); dispid 1;
    property Count: Integer readonly dispid 2;
    property Sink: Integer writeonly dispid 3;
  end;

  INoGuid = dispinterface
    procedure Run; dispid 1;
  end;

  TServer = class(TInterfacedObject, IGood)
  end;

  TEvent = procedure(A: Integer) of object;

var
  Good: IGood;
  NoGuid: INoGuid;
  Unknown: IInterface;
  N: Integer;
  Event: TEvent;

begin
  N := Good.Sink;
  Good.Count := N;
  NoGuid := Unknown as INoGuid;
  Event := Good.Run;
  Good.Run;
end.
