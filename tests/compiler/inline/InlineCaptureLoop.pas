// covers: LANG-566, CMP-1183
program InlineCaptureLoop;

{ A captured inline variable is a new instance at every execution of its declaration: closures of different iterations do
  not share it, closures of one iteration do, and share it with the scope's own code. An instance lives until its scope
  execution has ended and no closure refers to it: its interface-typed variable is released with the last closure. }

type
  TFunc = reference to function: Integer;
  TProc = reference to procedure;

  IGuard = interface
    ['{5E1D7C2A-90B4-4C6F-8A31-7F2B6D0E4C15}']
    procedure Touch;
  end;

  TGuard = class(TInterfacedObject, IGuard)
  private
    FName: string;
  public
    constructor Create(const AName: string);
    destructor Destroy; override;
    procedure Touch;
  end;

constructor TGuard.Create(const AName: string);
begin
  inherited Create;
  FName := AName;
  Writeln('create ', FName);
end;

destructor TGuard.Destroy;
begin
  Writeln('destroy ', FName);
  inherited Destroy;
end;

procedure TGuard.Touch;
begin
  Writeln('touch ', FName);
end;

function GuardName(I: Integer): string;
begin
  if I = 1 then Result := 'g1'
  else if I = 2 then Result := 'g2'
  else Result := 'g3';
end;

{ the example of LANG-566, in a routine }
procedure PerIteration;
var
  Fs: array[0..2] of TFunc;
  I, A, B, C, D: Integer;
begin
  for I := 0 to 2 do
  begin
    var N := I * 10;
    Fs[I] := function: Integer begin Result := N; Inc(N) end;
  end;
  A := Fs[0]();
  B := Fs[1]();
  C := Fs[2]();
  D := Fs[0]();
  Writeln(A, ' ', B, ' ', C, ' ', D);
end;

{ two closures of one iteration share the variable, and the scope's code sees their changes }
procedure SharedInOneExecution;
var
  Bump: array[1..2] of TProc;
  Get: array[1..2] of TFunc;
  I, A, B: Integer;
begin
  for I := 1 to 2 do
  begin
    var Total := I * 100;
    var Name: string;
    Name := GuardName(I);
    Bump[I] := procedure begin Total := Total + 1; Name := Name + '+' end;
    Get[I] := function: Integer begin Result := Total end;
    Bump[I]();
    Writeln('in scope ', Total, ' ', Name);
    Total := Total + 10;
  end;
  Bump[1]();
  Bump[1]();
  Bump[2]();
  A := Get[1]();
  B := Get[2]();
  Writeln('after ', A, ' ', B);
end;

{ an instance outlives the loop while a closure is kept; the other one goes at the end of its iteration }
procedure Outlives;
var
  Keep: TProc;
  I: Integer;
  Before: NativeInt;
begin
  Before := _LiveAllocCount;
  for I := 1 to 2 do
  begin
    var G: IGuard := TGuard.Create(GuardName(I));
    var P: TProc := procedure begin G.Touch end;
    if I = 1 then
      Keep := P;
    P();
    Writeln('end of iteration ', I);
  end;
  Writeln('after loop');
  Keep();
  Keep := nil;
  Writeln('released, leaked: ', _LiveAllocCount - Before);
end;

{ leaving the scope by Break, Continue, Exit and an exception releases an instance no closure holds }
procedure ExitPaths(Stop: Integer);
var
  I: Integer;
begin
  for I := 1 to 3 do
  begin
    var G: IGuard := TGuard.Create(GuardName(I));
    var Touch: TProc := procedure begin G.Touch end;
    if I = 1 then
      Continue;
    Touch();
    if I = Stop then
      Break;
    if Stop = 0 then
      Exit;
    if Stop < 0 then
      raise TObject.Create;
  end;
  Writeln('after break');
end;

{ repeat: the condition is inside the scope and reads the current instance }
procedure RepeatScope;
var
  Fs: array[1..3] of TFunc;
  Count, A, B, C: Integer;
begin
  Count := 0;
  repeat
    Inc(Count);
    var Step := Count * 2;
    Fs[Count] := function: Integer begin Inc(Step); Result := Step end;
    Fs[Count]();
  until Step >= 7;
  A := Fs[1]();
  B := Fs[2]();
  C := Fs[3]();
  Writeln('repeat ', Count, ' ', A, ' ', B, ' ', C);
end;

var
  Fs: array[0..2] of TFunc;
  I, A, B, C, D: Integer;
  Before: NativeInt;
begin
  PerIteration;
  SharedInOneExecution;
  Outlives;
  Before := _LiveAllocCount;
  ExitPaths(2);
  Writeln('exit:');
  ExitPaths(0);
  Writeln('raise:');
  try
    ExitPaths(-1);
  except
    on E: TObject do
      Writeln('caught');
  end;
  Writeln('paths leaked: ', _LiveAllocCount - Before);
  RepeatScope;

  { the same in the program block }
  for I := 0 to 2 do
  begin
    var N := I * 10;
    Fs[I] := function: Integer begin Result := N; Inc(N) end;
  end;
  A := Fs[0]();
  B := Fs[1]();
  C := Fs[2]();
  D := Fs[0]();
  Writeln(A, ' ', B, ' ', C, ' ', D);
  for I := 1 to 2 do
  begin
    var G: IGuard := TGuard.Create(GuardName(I));
    Fs[I] := function: Integer begin G.Touch; Result := 0 end;
    Writeln('main iteration ', I);
  end;
  Fs[2] := nil;
  Writeln('main end');
end.
