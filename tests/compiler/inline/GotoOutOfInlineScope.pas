// covers: LANG-571, CMP-1185
program GotoOutOfInlineScope;

{ A goto that leaves a declaration scope finalises the scope's managed variables before the statement at the label runs,
  inner scopes first; a backward goto to before the declaration executes it again with an empty variable (and a new block
  frame for a captured one). A goto inside one scope finalises nothing. }

type
  TFunc = reference to function: Integer;

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

function PassName(Pass: Integer): string;
begin
  case Pass of
    0: Result := 'first';
    1: Result := 'second';
  else
    Result := 'third';
  end;
end;

{ backward to before the declarations of a nested block }
procedure Again;
label
  Top;
var
  Pass: Integer;
begin
  Pass := 0;
  begin
  Top:
    Writeln('pass ', Pass);
    var S: string;
    Writeln('S is [', S, ']');
    S := S + PassName(Pass);
    var G: IGuard := TGuard.Create(S);
    Inc(Pass);
    if Pass < 3 then
      goto Top;
    Writeln('block ends');
  end;
  Writeln('after block');
end;

{ the same in the outermost statement list of a routine }
procedure AgainOutermost;
label
  Top;
var
  Pass: Integer;
begin
  Pass := 0;
Top:
  Writeln('pass ', Pass);
  var S: string;
  Writeln('S is [', S, ']');
  S := S + PassName(Pass);
  var G: IGuard := TGuard.Create('outermost ' + S);
  Inc(Pass);
  if Pass < 2 then
    goto Top;
  Writeln('body ends');
end;

{ out of two nested blocks }
procedure Leave(Jump: Boolean);
label
  Out;
begin
  begin
    var A: IGuard := TGuard.Create('outer');
    begin
      var B: IGuard := TGuard.Create('inner');
      if Jump then
        goto Out;
      B.Touch;
    end;
    A.Touch;
  end;
Out:
  Writeln('at Out');
end;

{ out of loop bodies, also of a repeat whose condition is inside the scope }
procedure LeaveLoops;
label
  Found, Done;
var
  I: Integer;
begin
  for I := 1 to 3 do
  begin
    var G: IGuard := TGuard.Create('for ' + PassName(I - 1));
    if I = 2 then
      goto Found;
  end;
  Writeln('not reached');
Found:
  Writeln('at Found');
  I := 0;
  repeat
    var R: IGuard := TGuard.Create('repeat ' + PassName(I));
    Inc(I);
    if I = 2 then
      goto Done;
  until I > 5;
  Writeln('not reached');
Done:
  Writeln('at Done');
end;

{ inside one scope, and inside the try part of a try statement }
procedure Stay;
label
  Skip, Leave;
begin
  try
    begin
      var G: IGuard := TGuard.Create('stay');
      goto Skip;
      Writeln('not reached');
    Skip:
      G.Touch;
      begin
        var H: IGuard := TGuard.Create('stay inner');
        goto Leave;
      end;
    Leave:
      Writeln('still in the scope of G');
    end;
    Writeln('try part ends');
  finally
    Writeln('finally');
  end;
end;

{ a captured variable: each execution of the declaration makes a new block frame }
procedure Frames;
label
  Next;
var
  Fs: array[0..2] of TFunc;
  I: Integer;
begin
  I := 0;
  begin
  Next:
    Writeln('frame ', I);
    var N := I * 10;
    var G: IGuard := TGuard.Create('frame ' + PassName(I));
    Fs[I] := function: Integer
      begin
        Result := N;
        Inc(N);
        if G = nil then
          Result := -1;
      end;
    Inc(I);
    if I < 3 then
      goto Next;
  end;
  Writeln('frames kept');
  Writeln(Fs[0](), ' ', Fs[1](), ' ', Fs[2](), ' ', Fs[0]());
  Fs[1] := nil;
  Writeln('second released');
end;

label
  Main;
var
  Round: Integer;
begin
  Again;
  AgainOutermost;
  Leave(True);
  Leave(False);
  LeaveLoops;
  Stay;
  Frames;
  Round := 0;
Main:
  Writeln('round ', Round);
  var Line: string;
  Writeln('Line is [', Line, ']');
  Line := 'round';
  var Last: IGuard := TGuard.Create('main ' + PassName(Round));
  Inc(Round);
  if Round < 2 then
    goto Main;
  Writeln('end of program');
end.
