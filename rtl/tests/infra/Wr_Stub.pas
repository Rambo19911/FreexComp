// phase: 4
// covers: RTL-481
program Wr_Stub;
{ TWriter (classes.md RTL-481) is declared so that component code compiles: a component's DefineProperties runs
  against a TWriter without effect (TComponent registers DesignLeft/DesignTop), and WriteRootComponent raises
  ENotImplemented until the run-time writer exists (Phase 8+). }
uses
  SysUtils, Classes, TestKit;

type
  TExposed = class(TComponent)
  public
    procedure Define(Filer: TFiler);
  end;

procedure TExposed.Define(Filer: TFiler);
begin
  DefineProperties(Filer);
end;

var
  S: TMemoryStream;
  W: TWriter;
  C: TExposed;
  Msg: string;
begin
  S := TMemoryStream.Create;
  W := TWriter.Create(S, 4096);
  C := TExposed.Create(nil);
  C.Name := 'C1';
  C.DesignInfo := $00200010;
  C.Define(W);
  CheckEqI(0, S.Size, 'DefineProperties against a writer writes nothing');
  Msg := '';
  try
    W.WriteRootComponent(C);
  except
    on E: ENotImplemented do
      Msg := E.ClassName + ': ' + E.Message;
  end;
  CheckEqS('ENotImplemented: TWriter.WriteRootComponent is not implemented', Msg, 'WriteRootComponent');
  C.Free;
  W.Free;
  S.Free;
  Done;
end.
