// covers: LANG-560, LANG-563
program InlineRepeatUntil;

{ The until condition belongs to the scope of the variables declared in the repeat list: it reads them, and they are
  finalised after it has been evaluated, once per iteration. Continue before the declaration leaves a managed variable
  empty in the condition. A variable captured by an anonymous method outlives the iteration. }

type
  TFunc = reference to function: string;

  IGuard = interface
    function Value: Integer;
  end;

  TGuard = class(TInterfacedObject, IGuard)
  private
    FValue: Integer;
  public
    constructor Create(AValue: Integer);
    destructor Destroy; override;
    function Value: Integer;
  end;

var
  Feed: Integer;

constructor TGuard.Create(AValue: Integer);
begin
  inherited Create;
  FValue := AValue;
  Writeln('create ', FValue);
end;

destructor TGuard.Destroy;
begin
  Writeln('destroy ', FValue);
  inherited Destroy;
end;

function TGuard.Value: Integer;
begin
  Writeln('value of ', FValue);
  Result := FValue;
end;

function Next: string;
begin
  Inc(Feed);
  if Feed = 1 then
    Result := 'alpha'
  else if Feed = 2 then
    Result := 'beta'
  else
    Result := '';
end;

procedure ReadLines;
var
  Before: NativeInt;
begin
  Before := _LiveAllocCount;
  repeat
    var Line := Next;
    var Shown := '<' + Line + '>';
    Writeln(Shown);
  until Line = '';
  Writeln('leaked: ', _LiveAllocCount - Before);
end;

procedure Guards;
var
  N: Integer;
begin
  N := 0;
  repeat
    Inc(N);
    var G: IGuard := TGuard.Create(N);
    Writeln('body ', N);
  until G.Value >= 2;
  Writeln('after guards');
end;

procedure Skips;
var
  N: Integer;
begin
  N := 0;
  repeat
    Inc(N);
    if N = 2 then
      Continue;
    var S: string;
    S := 'set';
    var G: IGuard := TGuard.Create(N * 10);
    Writeln('ran ', N);
  until (S = '') or (N >= 3);
  Writeln('stopped at ', N);
end;

function Captured: TFunc;
var
  N: Integer;
begin
  N := 0;
  repeat
    Inc(N);
    var Text := 'kept';
    if N = 2 then
      Result := function: string
        begin
          Result := Text + '!';
        end;
  until (N = 2) and (Text <> '');
end;

var
  F: TFunc;
begin
  ReadLines;
  Guards;
  Skips;
  F := Captured();
  Writeln(F());
  F := nil;
  Writeln('done');
end.
