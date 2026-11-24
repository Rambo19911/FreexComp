unit LifeTypes;
// what the library reports about itself, in a buffer of the host (compiled into both modules)

interface

type
  TLifePath = array[0..1023] of Char;

  TLifeInfo = record
    IsLibrary: Integer;
    HInstanceIsLoaded: Integer;
    IsConsole: Integer;
    MainThreadID: Cardinal;
    ParamCount: Integer;
    ParamStr0: TLifePath;
    ParamStr2: TLifePath;
    ModuleName: TLifePath;
  end;
  PLifeInfo = ^TLifeInfo;

implementation

end.
