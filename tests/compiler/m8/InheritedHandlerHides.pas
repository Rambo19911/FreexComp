// covers: LANG-960
program InheritedHandlerHides;

{ A published handler of a descendant class with the name of an ancestor's published handler hides it (a static method: no
  diagnostic, W1060 is for virtual methods only). MethodAddress on the descendant class finds the descendant's method, on the
  ancestor class the ancestor's; the hiding method's `inherited;` runs the ancestor's method with the same argument. This is how
  an event line of an ancestor's form reaches the handler of the descendant form class (form-inheritance, LANG-960). }

type
  TNotify = procedure(Sender: TObject) of object;

  TBaseDialog = class
  published
    procedure OkClick(Sender: TObject);
  end;

  TRenameDialog = class(TBaseDialog)
  published
    procedure OkClick(Sender: TObject);
  end;

procedure TBaseDialog.OkClick(Sender: TObject);
begin
  Writeln('base OkClick on ', ClassName, ' sender ', Sender.ClassName);
end;

procedure TRenameDialog.OkClick(Sender: TObject);
begin
  Writeln('rename OkClick before inherited');
  inherited;
  Writeln('rename OkClick after inherited');
end;

var
  D: TRenameDialog;
  B: TBaseDialog;
  M: TMethod;
  Handler: TNotify;
begin
  D := TRenameDialog.Create;
  B := TBaseDialog.Create;
  Writeln('descendant found ', TRenameDialog.MethodAddress('OkClick') <> nil);
  Writeln('descendant differs from ancestor ', TRenameDialog.MethodAddress('okclick') <> TBaseDialog.MethodAddress('OkClick'));
  Writeln('name ', TRenameDialog.MethodName(TRenameDialog.MethodAddress('OKCLICK')));

  M.Code := D.MethodAddress('OkClick');
  M.Data := Pointer(D);
  Handler := TNotify(M);
  Handler(B);

  M.Code := B.MethodAddress('OkClick');
  M.Data := Pointer(B);
  Handler := TNotify(M);
  Handler(D);

  Writeln('static call');
  TBaseDialog(D).OkClick(D);
  D.OkClick(B);
  B.Free;
  D.Free;
end.
