unit InlineUnitSectionsU;

interface

type
  IGuard = interface
    procedure Touch;
  end;

var
  Total: Integer;

function NewGuard(const Name: string): IGuard;

implementation

type
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

initialization
  Writeln('init');
  var Start := 40;
  var Whole := NewGuard('init whole');
  begin
    var Step := 2;
    var Block := NewGuard('init block');
    Block.Touch;
    Total := Start + Step;
  end;
  Writeln('init after block ', Total);
  for var I := 1 to 2 do
  begin
    var Each := NewGuard('init loop');
    Inc(Total);
  end;
  Whole.Touch;
  Writeln('end of init');
finalization
  Writeln('final');
  var Last := NewGuard('final whole');
  begin
    var Text := 'final block';
    var Block := NewGuard(Text);
    Block.Touch;
  end;
  Writeln('end of final ', Total);
end.
