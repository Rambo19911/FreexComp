// covers: RTL-151, RTL-152, RTL-143, ABI-072, ABI-075, ABI-076
program Obj_Lifecycle;
{ The construction and destruction protocol (ABI-075/076) against System's virtuals, as an order log: NewInstance,
  constructor bodies (inherited Create runs the body only), AfterConstruction, BeforeDestruction, destructor chain,
  FreeInstance; a constructor called on an instance (body only); a class whose NewInstance and FreeInstance are
  overridden. Leak checks through the RTL-064 counter.
  TODO(compiler): RTL-151 step 4, a constructor that raises (Destroy with Dealloc = 3, then re-raise, ABI-075), is
  compiler-emitted and not generated yet; its cases join this test when it is. }
uses TestKit;

type
  EFail = class
    Where: string;
  end;

  TBase = class
    Name: string;
    constructor Create(const AName: string; FailAt: Integer);
    destructor Destroy; override;
    procedure AfterConstruction; override;
    procedure BeforeDestruction; override;
    class function NewInstance: TObject; override;
    procedure FreeInstance; override;
  end;

  TChild = class(TBase)
    Items: array of string;
    constructor Create(const AName: string; FailAt: Integer);
    destructor Destroy; override;
  end;

var
  Log: string;

procedure Fail(const Where: string);
var
  E: EFail;
begin
  E := EFail.Create;
  E.Where := Where;
  raise E;
end;

class function TBase.NewInstance: TObject;
begin
  Log := Log + 'N';
  Result := inherited NewInstance;
end;

procedure TBase.FreeInstance;
begin
  Log := Log + 'F';
  inherited FreeInstance;
end;

constructor TBase.Create(const AName: string; FailAt: Integer);
begin
  inherited Create;
  Log := Log + 'c(base)';
  Name := AName + '!';
  if FailAt = 1 then
    Fail('base');
end;

destructor TBase.Destroy;
begin
  Log := Log + 'd(base)';
  inherited Destroy;
end;

procedure TBase.AfterConstruction;
begin
  Log := Log + 'A';
  inherited AfterConstruction;
end;

procedure TBase.BeforeDestruction;
begin
  Log := Log + 'B';
  inherited BeforeDestruction;
end;

constructor TChild.Create(const AName: string; FailAt: Integer);
begin
  inherited Create(AName, FailAt);
  Log := Log + 'c(child)';
  SetLength(Items, 2);
  Items[0] := Name + 'item';
  if FailAt = 2 then
    Fail('child');
end;

destructor TChild.Destroy;
begin
  Log := Log + 'd(child)';
  inherited Destroy;
end;

procedure TestNormal;
var
  C: TChild;
  O: TObject;
  Before: NativeInt;
begin
  Section('normal construction and destruction');
  Before := _LiveAllocCount;
  Log := '';
  C := TChild.Create('x', 0);
  CheckEqS('Nc(base)c(child)A', Log, 'NewInstance, bodies, AfterConstruction once');
  Log := '';
  C.Free;
  CheckEqS('Bd(child)d(base)F', Log, 'BeforeDestruction once, destructors, FreeInstance');
  Log := '';
  O := TBase.Create('y', 0);
  CheckEqS('Nc(base)A', Log, 'base class');
  Log := '';
  O.Destroy;
  CheckEqS('Bd(base)F', Log, 'Destroy through TObject');
  Log := '';
  CheckEqI(0, _LiveAllocCount - Before, 'leaked');
end;

procedure TestOnInstance;
var
  C: TChild;
  Before: NativeInt;
begin
  Section('constructor on an instance');
  Before := _LiveAllocCount;
  C := TChild.Create('a', 0);
  Log := '';
  C.Create('b', 0);
  CheckEqS('c(base)c(child)', Log, 'body only: no NewInstance, no AfterConstruction');
  CheckEqS('b!', C.Name, 'fields set by the second body');
  Log := '';
  C.Free;
  Log := '';
  CheckEqI(0, _LiveAllocCount - Before, 'leaked');
end;

begin
  TestNormal;
  TestOnInstance;
  Done;
end.
