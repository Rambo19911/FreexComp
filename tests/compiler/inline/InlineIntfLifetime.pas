// covers: LANG-563, LANG-565
program InlineIntfLifetime;

{ An interface-typed inline variable releases its object when its block is left: at the end of the block, before the next
  statement; two variables of one list in reverse order; nested scopes inner first; a variable of the routine's outermost
  list at the end of the routine. }

type
  IGuard = interface
    ['{3C5B0E52-7A1D-4F3E-9B6A-2D8C4E1F7A90}']
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

function NewGuard(const Name: string): IGuard;
begin
  Result := TGuard.Create(Name);
end;

procedure Run;
begin
  var Whole: IGuard := TGuard.Create('whole');
  Writeln('before block');
  begin
    var Guard := TGuard.Create('block') as IGuard;
    Guard.Touch;
  end;
  Writeln('after block');
  begin
    var First: IGuard := TGuard.Create('first');
    var Count := 2;
    var Second := NewGuard('second');
    Writeln('two guards ', Count);
  end;
  Writeln('after two');
  begin
    var Outer: IGuard := TGuard.Create('outer');
    begin
      var Inner: IGuard := TGuard.Create('inner');
      Inner.Touch;
    end;
    Writeln('between');
    begin
      var Again: IGuard := TGuard.Create('again');
      var Copy := Again;
      Copy.Touch;
    end;
    Outer.Touch;
  end;
  Writeln('after nested');
  begin
    var Unused: IGuard;
    Writeln('unused');
  end;
  if Whole <> nil then
  begin
    var Shared := Whole;
    Shared.Touch;
  end;
  Writeln('end of Run');
end;

begin
  Run;
  Writeln('after Run');
  begin
    var Main: IGuard := TGuard.Create('main block');
    Main.Touch;
  end;
  Writeln('done');
end.
