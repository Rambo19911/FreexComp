// covers: LANG-634, LANG-635, LANG-630, ABI-075, ABI-076
program ClassDestructorChain;

type
  TBase = class
    constructor Create;
    destructor Destroy; override;
    procedure AfterConstruction; override;
    procedure BeforeDestruction; override;
  end;

  TMiddle = class(TBase)
    destructor Destroy; override;
  end;

  TLeaf = class(TMiddle)
    constructor Create;
    destructor Destroy; override;
    destructor Close;
  end;

constructor TBase.Create;
begin
  Writeln('TBase.Create');
end;

destructor TBase.Destroy;
begin
  Writeln('TBase.Destroy');
  inherited Destroy;
end;

procedure TBase.AfterConstruction;
begin
  Writeln('AfterConstruction');
end;

procedure TBase.BeforeDestruction;
begin
  Writeln('BeforeDestruction');
end;

destructor TMiddle.Destroy;
begin
  Writeln('TMiddle.Destroy');
  inherited;
end;

constructor TLeaf.Create;
begin
  Writeln('TLeaf.Create begin');
  inherited Create;
  Writeln('TLeaf.Create end');
end;

destructor TLeaf.Destroy;
begin
  Writeln('TLeaf.Destroy');
  inherited Destroy;
end;

destructor TLeaf.Close;
begin
  Writeln('TLeaf.Close');
  inherited Destroy;
end;

var
  B: TBase;
  Before: NativeInt;
begin
  Before := _LiveAllocCount;
  B := TLeaf.Create;
  Writeln('--- Free through a TBase reference');
  B.Free;
  Writeln('--- Destroy');
  B := TMiddle.Create;
  B.Destroy;
  Writeln('--- another destructor');
  TLeaf.Create.Close;
  Writeln('leaked: ', _LiveAllocCount - Before);
end.
