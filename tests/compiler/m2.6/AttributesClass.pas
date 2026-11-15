// covers: LANG-760, LANG-761, LANG-762, ABI-140
program AttributesClass;

type
  TagAttribute = class(TCustomAttribute)
    Name: string;
    Level: Integer;
    constructor Create(const AName: string; ALevel: Integer); overload;
    constructor Create; overload;
  end;

  [Tag('first', 1 + 2)]
  [TagAttribute]
  TFoo = class
    [Tag('field', 0)]
    Value: Integer;
    [Tag]
    procedure Run([Tag] X: Integer);
  end;

  TBar = class
  end;

  PI64 = ^Int64;
  PP = ^Pointer;
  TThunk = function: TCustomAttribute;

constructor TagAttribute.Create(const AName: string; ALevel: Integer);
begin
  Name := AName;
  Level := ALevel;
end;

constructor TagAttribute.Create;
begin
  Name := 'default';
  Level := -1;
end;

procedure TFoo.Run(X: Integer);
begin
  Value := X;
end;

{ rtl-abi.md 10.1: AttrCount at +88, Attrs at +96 -> [AttrCount x (AttrVMT, CtorThunk)] }
procedure Show(Info: Pointer);
var
  Count, I: Integer;
  Table: Pointer;
  Thunk: TThunk;
  Attr: TCustomAttribute;
begin
  Count := PI64(NativeInt(Info) + 88)^;
  Table := PP(NativeInt(Info) + 96)^;
  Writeln('count ', Count);
  for I := 0 to Count - 1 do
  begin
    Thunk := TThunk(PP(NativeInt(Table) + I * 16 + 8)^);
    Attr := Thunk();
    Writeln(Attr.ClassName, ' ', TagAttribute(Attr).Name, ' ', TagAttribute(Attr).Level, ' ',
      PP(NativeInt(Table) + I * 16)^ = Pointer(TagAttribute));
    Attr.Free;
  end;
end;

begin
  Show(TypeInfo(TFoo));
  Show(TypeInfo(TBar));
end.