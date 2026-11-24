// covers: LANG-563, CMP-1180
program InlineExitPaths;

{ Every way out of a declaration scope finalises its managed variables before a statement outside the scope runs:
  Exit, Break, Continue, an exception caught outside the routine and one caught by an enclosing try of the same routine. }

uses SysUtils;

type
  IGuard = interface
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

function ByExit(Leave: Boolean): Integer;
begin
  Result := 0;
  try
    if Leave then
    begin
      var G: IGuard := TGuard.Create('exit');
      begin
        var Inner: IGuard := TGuard.Create('exit inner');
        Result := 1;
        Exit;
      end;
    end;
    Writeln('not left');
  finally
    Writeln('finally of ByExit');
  end;
end;

procedure ByBreakAndContinue;
var
  I: Integer;
  Name: string;
begin
  for I := 1 to 4 do
  begin
    Name := 'loop ' + IntToStr(I);
    var G: IGuard := TGuard.Create(Name);
    if I = 2 then
      Continue;
    if I = 3 then
      Break;
    G.Touch;
  end;
  Writeln('after for');
  I := 0;
  while True do
  begin
    Inc(I);
    var S := 'while ' + IntToStr(I);
    var G: IGuard := TGuard.Create(S);
    if I < 2 then
      Continue;
    Break;
  end;
  Writeln('after while');
end;

procedure Raises;
begin
  var Whole: IGuard := TGuard.Create('raises whole');
  begin
    var G: IGuard := TGuard.Create('raises block');
    raise Exception.Create('out');
  end;
end;

procedure CaughtInside;
begin
  try
    Writeln('try');
    begin
      var G: IGuard := TGuard.Create('caught');
      var H: IGuard := TGuard.Create('caught too');
      raise Exception.Create('inside');
    end;
  except
    on E: Exception do
      Writeln('handler ', E.Message);
  end;
  Writeln('after handler');
  try
    for var K := 1 to 2 do
    begin
      var Name := 'in loop ' + IntToStr(K);
      var G: IGuard := TGuard.Create(Name);
      if K = 2 then
        raise Exception.Create('in loop');
    end;
  except
    Writeln('caught in loop');
  end;
end;

begin
  Writeln(ByExit(True));
  Writeln(ByExit(False));
  ByBreakAndContinue;
  try
    Raises;
  except
    on E: Exception do
      Writeln('caught outside ', E.Message);
  end;
  CaughtInside;
  Writeln('done');
end.
