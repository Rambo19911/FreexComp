// covers: LANG-622, CMP-370, ABI-073
program MessageMethods;

{ message handlers found by TObject.Dispatch through the MsgTables of the class and its ancestors: own handlers
  (declared out of id order), an inherited handler, a handler replacing an ancestor's for the same id with
  'inherited' reaching the ancestor's handler, an id without handler (TObject.DefaultHandler does nothing), and a
  direct static call of a handler by name. }

type
  TMsg = record
    Id: Cardinal;
    Value: Integer;
    Result: Integer;
  end;

  TBase = class
    procedure HandleTwo(var M: TMsg); message 2;
    procedure HandleOne(var M: TMsg); message 1;
  end;

  TDerived = class(TBase)
    procedure Handle300(var M: TMsg); message 300;
    procedure DerivedOne(var M: TMsg); message 1;
    procedure HandleThree(var M: TMsg); message 3;
  end;

procedure TBase.HandleOne(var M: TMsg);
begin
  Writeln('TBase.HandleOne ', M.Value);
  M.Result := M.Value * 10;
end;

procedure TBase.HandleTwo(var M: TMsg);
begin
  Writeln('TBase.HandleTwo ', M.Value);
end;

procedure TDerived.Handle300(var M: TMsg);
begin
  Writeln('TDerived.Handle300 ', M.Value);
  M.Result := 300;
end;

procedure TDerived.DerivedOne(var M: TMsg);
begin
  Writeln('TDerived.DerivedOne ', M.Value);
  inherited;
  M.Result := M.Result + 1;
end;

procedure TDerived.HandleThree(var M: TMsg);
begin
  Writeln('TDerived.HandleThree ', M.Value);
  M.Result := -3;
end;

procedure Send(O: TObject; Id: Cardinal; Value: Integer);
var
  M: TMsg;
begin
  M.Id := Id;
  M.Value := Value;
  M.Result := 0;
  O.Dispatch(M);
  Writeln('id ', Id, ' result ', M.Result);
end;

var
  B: TBase;
  D: TDerived;
  M: TMsg;
begin
  B := TBase.Create;
  D := TDerived.Create;
  Send(B, 1, 5);
  Send(B, 2, 6);
  Send(B, 3, 7);
  Send(D, 1, 8);
  Send(D, 2, 9);
  Send(D, 3, 4);
  Send(D, 300, 1);
  Send(D, 299, 2);
  M.Value := 11;
  D.HandleThree(M);
  Writeln('direct ', M.Result);
  D.Free;
  B.Free;
end.
