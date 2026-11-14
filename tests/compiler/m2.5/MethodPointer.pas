// covers: LANG-481, CMP-378, ABI-080
program MethodPointer;

{ Method pointers (procedure/function of object): created from Obj.Method, from a method name inside a method (Self),
  from TClass.ClassMethod (data = the class); nil, Assigned, copies, an event property with a field accessor that is
  fired, a parameter and a result of method-pointer type, and TMethod(E).Code/Data. }

type
  TNotifyEvent = procedure(Sender: TObject) of object;
  TCalc = function(X: Integer): Integer of object;
  TKindFunc = function: string of object;

  TButton = class
  private
    FOnClick: TNotifyEvent;
  public
    Name: string;
    procedure Click;
    property OnClick: TNotifyEvent read FOnClick write FOnClick;
  end;

  TForm = class
    Factor: Integer;
    procedure ButtonClick(Sender: TObject);
    function Times(X: Integer): Integer;
    function GetCalc: TCalc;
    class function Kind: string;
  end;

procedure TButton.Click;
begin
  if Assigned(FOnClick) then
    FOnClick(Self)
  else
    Writeln(Name, ': no handler');
end;

procedure TForm.ButtonClick(Sender: TObject);
begin
  Writeln('clicked ', (Sender as TButton).Name, ' factor ', Factor);
end;

function TForm.Times(X: Integer): Integer;
begin
  Result := X * Factor;
end;

function TForm.GetCalc: TCalc;
begin
  Result := Times;
end;

class function TForm.Kind: string;
begin
  Result := 'form class ' + ClassName;
end;

function Run(C: TCalc; X: Integer): Integer;
begin
  Result := C(X);
end;

var
  B: TButton;
  F, G: TForm;
  C, D: TCalc;
  K: TKindFunc;
  E: TNotifyEvent;
begin
  B := TButton.Create;
  B.Name := 'ok';
  F := TForm.Create;
  F.Factor := 3;
  G := TForm.Create;
  G.Factor := 10;
  B.Click;
  B.OnClick := F.ButtonClick;
  B.Click;
  F.Factor := 4;
  B.Click;
  E := B.OnClick;
  Writeln(Assigned(E), ' ', TMethod(E).Data = Pointer(F), ' ', TMethod(E).Code <> nil);
  B.OnClick := nil;
  B.Click;
  C := G.Times;
  D := C;
  Writeln(C(2), ' ', D(5), ' ', Run(F.Times, 6), ' ', Run(G.GetCalc, 7));
  C := F.GetCalc;
  Writeln(C(1), ' ', @C <> nil);
  K := TForm.Kind;
  Writeln(K);
  C := nil;
  Writeln(Assigned(C), ' ', Assigned(D));
  B.Free;
  F.Free;
  G.Free;
end.
