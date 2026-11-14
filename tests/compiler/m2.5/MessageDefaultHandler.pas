// covers: LANG-622, CMP-370, ABI-073
program MessageDefaultHandler;

{ An unhandled id reaches the overridden (virtual) DefaultHandler; 'inherited' in a handler without an ancestor
  handler for its id reaches DefaultHandler too (dispatched virtually, with the handler's own message), and
  'inherited' in a handler with an ancestor handler reaches that one first. }

type
  TMsg = record
    Id: Cardinal;
    Tag: Integer;
  end;

  TBase = class
    procedure DefaultHandler(var Message); override;
  end;

  TChild = class(TBase)
    procedure OnFive(var M: TMsg); message 5;
  end;

  TGrand = class(TChild)
    procedure OnFiveAgain(var M: TMsg); message 5;
    procedure OnSix(var M: TMsg); message 6;
    procedure DefaultHandler(var Message); override;
  end;

procedure TBase.DefaultHandler(var Message);
begin
  Writeln('TBase.DefaultHandler id=', TMsg(Message).Id, ' tag=', TMsg(Message).Tag);
end;

procedure TChild.OnFive(var M: TMsg);
begin
  Writeln('TChild.OnFive ', M.Tag);
  inherited;
end;

procedure TGrand.OnFiveAgain(var M: TMsg);
begin
  Writeln('TGrand.OnFiveAgain ', M.Tag);
  inherited;
end;

procedure TGrand.OnSix(var M: TMsg);
begin
  Writeln('TGrand.OnSix ', M.Tag);
  M.Tag := M.Tag * 100;
  inherited;
end;

procedure TGrand.DefaultHandler(var Message);
begin
  Writeln('TGrand.DefaultHandler');
  inherited;
end;

procedure Send(O: TObject; Id: Cardinal; Tag: Integer);
var
  M: TMsg;
begin
  M.Id := Id;
  M.Tag := Tag;
  O.Dispatch(M);
end;

var
  C: TChild;
  G: TGrand;
  O: TObject;
begin
  C := TChild.Create;
  G := TGrand.Create;
  O := TObject.Create;
  Send(C, 7, 1);
  Send(C, 5, 2);
  Send(G, 5, 3);
  Send(G, 6, 4);
  Send(G, 9, 5);
  Send(O, 5, 6);
  Writeln('done');
  O.Free;
  G.Free;
  C.Free;
end.
