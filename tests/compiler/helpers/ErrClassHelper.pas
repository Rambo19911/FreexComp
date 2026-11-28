// covers: LANG-990, LANG-991, LANG-754
program ErrClassHelper;

type
  TFoo = class
  strict private
    FSecret: Integer;
  public
    FOpen: Integer;
    procedure Reset;
  end;

  TBar = class
  end;

  IFoo = interface
    procedure Run;
  end;

  TFooHelper = class helper for TFoo
    function Peek: Integer;
  end;

  TIntHelper = record helper for Integer
    function Twice: Integer;
  end;

  TBarHelper = class helper for TBar
  end;

  TFieldHelper = class helper for TFoo
    FCount: Integer;
  end;

  TVarHelper = record helper for Integer
  var
    FLast: Integer;
  end;

  TVirtualHelper = class helper for TFoo
    procedure P; virtual;
  end;

  TAbstractHelper = class helper for TFoo
    procedure Q; virtual; abstract;
  end;

  TCtorHelper = class helper for TFoo
    constructor Create;
  end;

  TDefaultHelper = class helper for TFoo
    function GetItem(I: Integer): Integer;
    property Items[I: Integer]: Integer read GetItem; default;
  end;

  TNotAClass = class helper for Integer
  end;

  TClassAsRecord = record helper for TFoo
  end;

  TInterfaceHelper = record helper for IFoo
  end;

  TIntHelper2 = record helper(TIntHelper) for Integer
  end;

  TRecordAncestor = class helper(TIntHelper) for TFoo
  end;

  TOtherClassAncestor = class helper(TBarHelper) for TFoo
  end;

  TClassAncestor = class helper(TFoo) for TFoo
  end;

var
  V: TFooHelper;

procedure TFoo.Reset;
begin
  FSecret := 0;
end;

function TFooHelper.Peek: Integer;
begin
  Result := FSecret + Self.FSecret + FOpen;
end;

function TIntHelper.Twice: Integer;
begin
  Result := Self * 2;
end;

function TDefaultHelper.GetItem(I: Integer): Integer;
begin
  Result := I;
end;

begin
end.
